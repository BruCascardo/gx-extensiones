using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.Common.Services;
using Artech.Architecture.UI.Framework.Services;
using Artech.Genexus.Common.Objects;

namespace GxSdtJson
{
    static class SdtJsonCommands
    {
        const string Title = "GxSdtJson";

        public static void Copy()
        {
            SdtDocument document = SdtDocument.Active();
            if (document == null)
                return;
            try
            {
                document.Flush();
                SdtNode node = SdtReader.Read(document.Sdt, document.Structure);
                Clipboard.SetText(SdtJsonFormat.Write(node));
                Status($"{Title}: estructura de {document.Sdt.Name} copiada como JSON ({node.CountMembers()} miembros)");
            }
            catch (Exception ex)
            {
                ShowError("No se pudo copiar la estructura", ex);
            }
        }

        /// <summary>Con un SDT abierto importa en él; si no (o a elección), crea uno nuevo.</summary>
        public static void Import()
        {
            SdtDocument document = SdtDocument.Active();
            if (document != null && document.ReadOnly)
                document = null;

            KBModel model = document?.Sdt.Model ?? (UIServices.KB.CurrentKB != null ? UIServices.KB.CurrentModel : null);
            if (model == null)
                return;

            KBObject newParent = NewSdtParent(model);
            document?.Flush();

            List<string> warnings = null;
            string done = null;
            using (var dialog = new ImportDialog(
                document?.Sdt.Name,
                document?.Structure.Root.Items.Count ?? 0,
                newParent is Module module && module.IsRoot ? null : newParent.Name,
                request =>
                {
                    try
                    {
                        var writer = new SdtWriter(model);
                        done = request.Mode == ImportMode.New
                            ? CreateNew(model, newParent, request, writer)
                            : ApplyToDocument(document, request, writer);
                        warnings = writer.Warnings;
                        return done != null;
                    }
                    catch (Exception ex)
                    {
                        ShowError("No se pudo importar", ex);
                        return false;
                    }
                }))
            {
                if (UIServices.Environment.ShowModalDialog(dialog) != DialogResult.OK)
                    return;
            }

            Report(done, warnings);
        }

        static string ApplyToDocument(SdtDocument document, ImportRequest request, SdtWriter writer)
        {
            var root = document.Structure.Root;
            if (request.Mode == ImportMode.Replace)
            {
                SdtWriter.Clear(root);
                SdtWriter.ApplyRootCollection(root, request.Node);
            }
            writer.AddItems(root, request.Node.Items);
            document.Reload();

            string verb = request.Mode == ImportMode.Replace ? "reemplazada" : "importada";
            return $"Estructura de {document.Sdt.Name} {verb}: {writer.Created} ítems. Falta grabar el SDT.";
        }

        static string CreateNew(KBModel model, KBObject parent, ImportRequest request, SdtWriter writer)
        {
            var sdt = new SDT(model)
            {
                Name = request.NewName,
                Description = string.IsNullOrEmpty(request.Node.Description) ? request.NewName : request.Node.Description,
                Parent = parent,
            };
            if (SDT.Get(model, new QualifiedName(sdt)) != null)
            {
                MessageBox.Show($"Ya existe un SDT {sdt.QualifiedName}. Elegí otro nombre, o abrilo e importá ahí.", Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            SdtWriter.ApplyRootCollection(sdt.SDTStructure.Root, request.Node);
            writer.AddItems(sdt.SDTStructure.Root, request.Node.Items);
            sdt.Save();

            UIServices.DocumentManager.OpenDocument(sdt, OpenDocumentOptions.CurrentVersion);
            return $"SDT {sdt.QualifiedName} creado con {writer.Created} ítems.";
        }

        /// <summary>Donde se crea un SDT nuevo: junto al objeto activo (su carpeta o módulo) o en la raíz.</summary>
        static KBObject NewSdtParent(KBModel model)
        {
            KBObject active = UIServices.Environment.ActiveDocument?.Object;
            if (active != null && active.Model == model && active.Parent != null)
                return active.Parent;
            return Module.GetRoot(model);
        }

        internal static void Report(string done, List<string> warnings)
        {
            if (done == null)
                return;
            if (warnings == null || warnings.Count == 0)
            {
                Status($"{Title}: {done}");
                return;
            }

            CommonServices.Output.AddLine($"{Title}: {done}");
            foreach (string warning in warnings)
                CommonServices.Output.AddWarningLine($"{Title}: {warning}");

            const int Max = 15;
            string list = string.Join(Environment.NewLine, warnings.Take(Max).Select(w => "• " + w));
            if (warnings.Count > Max)
                list += Environment.NewLine + $"… y {warnings.Count - Max} más (ver Output).";
            MessageBox.Show(done + Environment.NewLine + Environment.NewLine + "Con avisos:" + Environment.NewLine + list,
                Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        internal static void ShowError(string what, Exception ex)
        {
            CommonServices.Output.AddErrorLine($"{Title}: {what}: {ex}");
            MessageBox.Show($"{what}:{Environment.NewLine}{ex.Message}", Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        internal static void Status(string message)
        {
            if (UIServices.IsStatusBarAvailable)
                UIServices.StatusBar.DisplayText(message);
        }
    }
}
