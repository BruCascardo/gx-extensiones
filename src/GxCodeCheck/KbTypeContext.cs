using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common;
using Artech.Genexus.Common.CustomTypes;
using Artech.Genexus.Common.Helpers;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;
using Artech.Genexus.Common.Parts.SDT;
using Artech.Common.Properties;
using Artech.Udm.Framework;
using Attribute = Artech.Genexus.Common.Objects.Attribute;

namespace GxCodeCheck
{
    /// <summary>
    /// Los tipos del objeto que se está editando y de lo que usa, leídos de la KB abierta: las variables (ya
    /// en memoria), los atributos, los miembros de los SDT, los dominios y el parm() de cada procedimiento.
    /// </summary>
    /// <remarks>
    /// Leer un objeto de la KB es una consulta a la base: lo que no es del objeto editado se guarda entre
    /// guardados y se descarta cuando ese objeto se guarda (<see cref="ObjectSaved"/>) o se cierra la KB.
    /// </remarks>
    sealed class KbTypeContext : ITypeContext
    {
        // Nombre como está escrito, desde un módulo → procedimiento (nombre calificado), o null si no es uno
        static readonly Dictionary<string, string> s_Resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, CallSignature> s_Signatures = new Dictionary<string, CallSignature>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, TypeInfo> s_Attributes = new Dictionary<string, TypeInfo>(StringComparer.OrdinalIgnoreCase);
        // Los miembros de cada SDT (nombre calificado del SDT y camino del nivel); Missing si no existe
        static readonly Dictionary<string, TypeInfo> s_Members = new Dictionary<string, TypeInfo>(StringComparer.OrdinalIgnoreCase);
        static readonly TypeInfo Missing = new TypeInfo(TypeGroup.Unknown, "no existe");
        // Nombre como está escrito, desde un módulo → dominio, o null si no es uno
        static readonly Dictionary<string, EnumDomain> s_Domains = new Dictionary<string, EnumDomain>(StringComparer.OrdinalIgnoreCase);
        // La descripción de cada tipo de SDT o Business Component (leerla cuesta milisegundos), por su referencia
        static readonly Dictionary<string, TypeInfo> s_Described = new Dictionary<string, TypeInfo>();
        // Nombre como está escrito, desde un módulo → si es un objeto de la KB (de cualquier tipo)
        static readonly Dictionary<string, bool> s_Exists = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        // Los valores de cada dominio enumerado, por su Id
        static readonly Dictionary<int, EnumDomain> s_Enums = new Dictionary<int, EnumDomain>();

        readonly KBObject m_Object;
        readonly VariablesPart m_Variables;
        readonly Dictionary<string, TypeInfo> m_VariableTypes = new Dictionary<string, TypeInfo>(StringComparer.OrdinalIgnoreCase);
        // El nivel del SDT de cada variable o miembro
        readonly Dictionary<object, SDTLevel> m_Levels = new Dictionary<object, SDTLevel>();
        readonly Dictionary<SDTLevel, string> m_LevelKeys = new Dictionary<SDTLevel, string>();
        // El módulo del objeto: leerlo cuesta (una consulta por cada nombre que podría ser un objeto)
        Module m_Module;
        string m_From;
        readonly Stopwatch m_VariablesTime = new Stopwatch();
        readonly Stopwatch m_KbTime = new Stopwatch();
        int m_KbReads;

        public KbTypeContext(KBObject obj)
        {
            m_Object = obj;
            m_Variables = obj.Parts.Get<VariablesPart>();
        }

        Module Module => m_Module ?? (m_Module = m_Object.Module);

        string From => m_From ?? (m_From = Module?.QualifiedName.ToString() ?? "");

        /// <summary>Para el log: cuánto tardó cada cosa.</summary>
        public string Timings => $"variables {m_VariablesTime.ElapsedMilliseconds} ms, {m_KbReads} lectura(s) de la KB {m_KbTime.ElapsedMilliseconds} ms";

        /// <summary>Se guardó un objeto: lo que se sabía de él (o de algo que dependa de él) ya no vale.</summary>
        public static void ObjectSaved(KBObject obj)
        {
            // Un objeto nuevo: los nombres que no se encontraban pueden ser él
            foreach (string key in s_Exists.Where(r => !r.Value && r.Key.EndsWith(obj.Name, StringComparison.OrdinalIgnoreCase)).Select(r => r.Key).ToList())
                s_Exists.Remove(key);
            if (obj is Procedure)
            {
                string name = obj.QualifiedName.ToString();
                s_Signatures.Remove(name);
                // Un procedimiento nuevo: los nombres que no se encontraban pueden ser él
                foreach (string key in s_Resolved.Where(r => r.Value == null && r.Key.EndsWith(obj.Name, StringComparison.OrdinalIgnoreCase)).Select(r => r.Key).ToList())
                    s_Resolved.Remove(key);
            }
            else if (!(obj is WebPanel))
            {
                // Un SDT, una transacción, un dominio...: cambian tipos de parámetros, atributos y miembros
                s_Signatures.Clear();
                s_Attributes.Clear();
                s_Members.Clear();
                s_Domains.Clear();
                s_Described.Clear();
                s_Enums.Clear();
            }
        }

        public static void Clear()
        {
            s_Exists.Clear();
            s_Resolved.Clear();
            s_Signatures.Clear();
            s_Attributes.Clear();
            s_Members.Clear();
            s_Domains.Clear();
            s_Described.Clear();
            s_Enums.Clear();
        }

        public TypeInfo Variable(string name)
        {
            if (m_VariableTypes.TryGetValue(name, out TypeInfo type))
                return type;
            m_VariablesTime.Start();
            try
            {
                Variable variable = m_Variables?.GetVariable(name);
                type = variable == null ? null : Describe(variable, variable.DomainBasedOn);
            }
            finally
            {
                m_VariablesTime.Stop();
            }
            m_VariableTypes[name] = type;
            return type;
        }

        public TypeInfo Attribute(string name)
        {
            if (s_Attributes.TryGetValue(name, out TypeInfo type))
                return type;
            type = Read(() =>
            {
                Attribute attribute = Artech.Genexus.Common.Objects.Attribute.Get(m_Object.Model, name);
                return attribute == null ? null : Describe(attribute, attribute.DomainBasedOn);
            });
            s_Attributes[name] = type;
            return type;
        }

        public TypeInfo Member(TypeInfo owner, string name, out bool missing)
        {
            missing = false;
            SDTLevel level = Level(owner);
            if (level == null)
                return null;
            if (!m_LevelKeys.TryGetValue(level, out string levelKey))
                m_LevelKeys[level] = levelKey = level.SDT?.QualifiedName + "|" + level.FullName + "|";
            string key = levelKey + name;
            if (!s_Members.TryGetValue(key, out TypeInfo type))
            {
                type = Read(() =>
                {
                    // Sin distinguir mayúsculas, como GeneXus
                    object found = level.Items.Cast<object>().FirstOrDefault(i => string.Equals((i as INamedObject)?.Name, name, StringComparison.OrdinalIgnoreCase));
                    switch (found)
                    {
                        case SDTItem item:
                            return Describe(item, item.DomainBasedOn);
                        case SDTLevel sublevel:
                            // Del nivel solo se sabe que es un SDT
                            return new TypeInfo(TypeGroup.Sdt, owner.Display + "." + sublevel.Name, isCollection: sublevel.IsCollection, exact: false, handle: sublevel);
                        case null:
                            return Missing;
                        default:
                            return null;
                    }
                });
                s_Members[key] = type;
            }
            if (type == Missing)
            {
                missing = true;
                return null;
            }
            return type;
        }

        public IEnumerable<string> Members(TypeInfo owner)
        {
            SDTLevel level = Level(owner);
            if (level == null)
                return Enumerable.Empty<string>();
            return Read(() => level.Items.Cast<object>().Select(i => (i as INamedObject)?.Name).Where(n => n != null).ToList()) ?? Enumerable.Empty<string>();
        }

        SDTLevel Level(TypeInfo owner)
        {
            if (owner.Group != TypeGroup.Sdt || owner.Handle == null)
                return null;
            if (!m_Levels.TryGetValue(owner.Handle, out SDTLevel level))
            {
                level = owner.Handle as SDTLevel ?? Read(() => StructureHelper.GetAssociatedLevel((ITypedObject)owner.Handle) as SDTLevel);
                m_Levels[owner.Handle] = level;
            }
            return level;
        }

        public EnumDomain Domain(string name)
        {
            string key = From + "|" + name;
            if (s_Domains.TryGetValue(key, out EnumDomain domain))
                return domain;
            domain = Read(() =>
            {
                Domain found = Artech.Genexus.Common.Objects.Domain.ResolveName(Module, name);
                if (found == null)
                    return null;
                var result = new EnumDomain { Name = found.Name, Type = Describe(found, found) };
                result.Values = result.Type.Enum?.Values;
                return result;
            });
            s_Domains[key] = domain;
            return domain;
        }

        public CallSignature Procedure(string name)
        {
            string from = From;
            string key = from + "|" + name;
            if (!s_Resolved.TryGetValue(key, out string qualified))
            {
                // Como lo resuelve GeneXus: desde el módulo del objeto
                qualified = Read(() =>
                {
                    EntityKey entity = Artech.Genexus.Common.Objects.Procedure.ResolveNameKey(Module, name);
                    return entity == null ? null : (KBObject.Get(m_Object.Model, entity) as Procedure)?.QualifiedName.ToString();
                });
                s_Resolved[key] = qualified;
            }
            if (qualified == null)
                return null;
            if (s_Signatures.TryGetValue(qualified, out CallSignature signature))
                return signature;
            signature = Read(() => ReadSignature(Artech.Genexus.Common.Objects.Procedure.Get(m_Object.Model, new QualifiedName(qualified))));
            s_Signatures[qualified] = signature;
            return signature;
        }

        public bool ObjectExists(string name)
        {
            string key = From + "|" + name;
            if (s_Exists.TryGetValue(key, out bool exists))
                return exists;
            // Ante la duda (no se pudo leer), existe: un error que no existe confunde más que uno que se escapa
            string found = Read(() =>
            {
                foreach (Guid? type in CallableTypes)
                {
                    if (m_Object.Model.Objects.ResolveName(Module, type, name, out ResolveResult result) && result.Match != null)
                        return result.Match.QualifiedName.ToString();
                }
                return "";
            });
            exists = found == null || found.Length > 0;
            if (found != null)
                s_Exists[key] = exists;
            return exists;
        }

        // Lo que se puede llamar: cualquier tipo primero, y los que se llaman, por si el nombre se repite entre tipos
        static readonly Guid?[] CallableTypes = { null, ObjClass.Procedure, ObjClass.WebPanel, ObjClass.Transaction, ObjClass.DataProvider };

        bool m_OwnRead;
        CallSignature m_Own;

        public CallSignature Own()
        {
            if (!m_OwnRead)
            {
                m_OwnRead = true;
                // El que se está editando: su parm() en memoria, sin guardar entre chequeos
                m_Own = Read(() => ReadSignature(m_Object));
            }
            return m_Own;
        }

        static CallSignature ReadSignature(KBObject obj)
        {
            Signature parm = (obj as ICallableInfo)?.GetSignatures().FirstOrDefault();
            if (parm == null)
                return null;
            var signature = new CallSignature { Name = obj.Name };
            foreach (Parameter parameter in parm.Parameters)
            {
                string parameterName = parameter.Name ?? "";
                if (!parameter.IsAttribute && !parameterName.StartsWith("&"))
                    parameterName = "&" + parameterName;
                ITypedObject typed = parameter.Object;
                signature.Parameters.Add(new CallParameter
                {
                    Name = parameterName,
                    Direction = parameter.Accessor == RuleDefinition.ParameterAccess.PARM_IN ? ParameterDirection.In
                        : parameter.Accessor == RuleDefinition.ParameterAccess.PARM_OUT ? ParameterDirection.Out : ParameterDirection.InOut,
                    Type = typed == null ? TypeInfo.Unknown : Describe(typed, (typed as Variable)?.DomainBasedOn ?? (typed as Attribute)?.DomainBasedOn),
                });
            }
            return signature;
        }

        T Read<T>(Func<T> read) where T : class
        {
            m_KbReads++;
            m_KbTime.Start();
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                Log.Write("Chequeo de tipos: no se pudo leer de la KB: " + ex.Message);
                return null;
            }
            finally
            {
                m_KbTime.Stop();
            }
        }

        static TypeInfo Describe(ITypedObject typed, Domain domain)
        {
            TypeInfo type = DescribeType(typed, domain);
            if (domain != null && type.Group != TypeGroup.Sdt && type.Group != TypeGroup.Bc && domain.IsEnumerated)
                type.Enum = EnumOf(domain);
            return type;
        }

        /// <summary>Los valores del dominio enumerado: su nombre (ACTIVO) por su valor ("AC").</summary>
        static EnumDomain EnumOf(Domain domain)
        {
            if (s_Enums.TryGetValue(domain.Id, out EnumDomain known))
                return known;
            var result = new EnumDomain { Name = domain.QualifiedName.ToString(), Values = new List<string>() };
            foreach (EnumValue value in domain.GetPropertyValue<EnumValues>("IDEnumDefinedValues")?.Values ?? new List<EnumValue>())
            {
                result.Values.Add(value.Name);
                string literal = EnumDomain.Unquote(value.Value ?? "");
                if (!result.NameByValue.ContainsKey(literal))
                    result.NameByValue[literal] = value.Name;
            }
            s_Enums[domain.Id] = result;
            return result;
        }

        static TypeInfo DescribeType(ITypedObject typed, Domain domain)
        {
            string basedOn = domain != null ? domain.QualifiedName + " " : "";
            bool collection = typed.IsCollection;
            switch (typed.Type)
            {
                case eDBType.CHARACTER:
                    return new TypeInfo(TypeGroup.Text, $"{basedOn}Character({typed.Length})", isCollection: collection) { Length = typed.Length };
                case eDBType.VARCHAR:
                    return new TypeInfo(TypeGroup.Text, $"{basedOn}VarChar({typed.Length})", isCollection: collection) { Length = typed.Length };
                case eDBType.LONGVARCHAR:
                    return new TypeInfo(TypeGroup.Text, $"{basedOn}LongVarChar", isCollection: collection);
                case eDBType.NUMERIC:
                case eDBType.INT:
                case eDBType.PACKED:
                case eDBType.ZONED:
                    return new TypeInfo(TypeGroup.Number, $"{basedOn}Numeric({typed.Length}.{typed.Decimals})", isCollection: collection);
                case eDBType.DATE:
                    return new TypeInfo(TypeGroup.Date, $"{basedOn}Date", isCollection: collection);
                case eDBType.DATETIME:
                    return new TypeInfo(TypeGroup.Date, $"{basedOn}DateTime", isCollection: collection);
                case eDBType.Boolean:
                    return new TypeInfo(TypeGroup.Bool, $"{basedOn}Boolean", isCollection: collection);
                case eDBType.GUID:
                    return new TypeInfo(TypeGroup.Guid, $"{basedOn}GUID", isCollection: collection);
                case eDBType.GX_SDT:
                case eDBType.GX_BUSCOMP:
                case eDBType.GX_BUSCOMP_LEVEL:
                {
                    // "254:<StructureTypeReference>...<Id>62</Id>...": identifica el SDT, y leerlo no cuesta nada
                    string reference = (typed as IPropertyBag)?.GetPropertyValue<object>("ATTCUSTOMTYPE")?.ToString();
                    string cacheKey = reference == null ? null : $"{reference}|{collection}|{domain?.Id}";
                    if (cacheKey != null && s_Described.TryGetValue(cacheKey, out TypeInfo described))
                        return described;
                    TypeInfo result = DescribeStructure(typed, collection);
                    if (cacheKey != null)
                        s_Described[cacheKey] = result;
                    return result;
                }
                default:
                    return new TypeInfo(TypeGroup.Other, typed.Type.ToString(), isCollection: collection);
            }
        }

        static TypeInfo DescribeStructure(ITypedObject typed, bool collection)
        {
            string structure = StructureHelper.GetAssociatedObject(typed)?.QualifiedName.ToString();
            TypeGroup group = typed.Type == eDBType.GX_SDT ? TypeGroup.Sdt : TypeGroup.Bc;
            object handle = typed;
            if (group == TypeGroup.Sdt)
            {
                // Un SDT que es colección por su tipo (Messages), aunque la variable no esté marcada como colección
                var level = StructureHelper.GetAssociatedLevelForItem(typed, out bool byType);
                collection |= byType;
                handle = level as SDTLevel ?? (object)typed;
            }
            return new TypeInfo(group, structure ?? (group == TypeGroup.Sdt ? "SDT" : "Business Component"), structure, collection, handle: handle);
        }
    }
}
