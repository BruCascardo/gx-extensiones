using System.Linq;
using System.Windows.Forms;
using Artech.Architecture.UI.Framework.Objects;
using Artech.Architecture.UI.Framework.Services;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;

namespace GxSdtJson
{
    /// <summary>
    /// El SDT abierto en el documento activo, con su editor de estructura. El editor guarda una
    /// copia de la estructura: antes de leer el part hay que bajarle los cambios (<see cref="Flush"/>)
    /// y después de modificarlo, recargarlo (<see cref="Reload"/>).
    /// </summary>
    sealed class SdtDocument
    {
        public IGxDocument Document { get; private set; }
        public SDT Sdt { get; private set; }
        public SDTStructurePart Structure { get; private set; }

        IGxDocumentPart m_DocumentPart;
        IGxView m_View;

        /// <summary>Barato: para habilitar los comandos sin recorrer los controles.</summary>
        public static bool IsActive() =>
            UIServices.IsEnvironmentAvailable && UIServices.Environment.ActiveDocument?.Object is SDT;

        public static SdtDocument Active()
        {
            if (!UIServices.IsEnvironmentAvailable)
                return null;
            IGxDocument document = UIServices.Environment.ActiveDocument;
            if (!(document?.Object is SDT sdt))
                return null;

            IGxDocumentPart documentPart = document.GetDocumentPart(typeof(SDTStructurePart).GUID)
                ?? document.GetDocumentParts().FirstOrDefault(p => p.Part is SDTStructurePart);
            var result = new SdtDocument
            {
                Document = document,
                Sdt = sdt,
                m_DocumentPart = documentPart,
                Structure = documentPart?.Part as SDTStructurePart ?? sdt.SDTStructure,
            };
            if (documentPart != null)
                result.m_View = FindView(documentPart);
            return result;
        }

        public bool ReadOnly => Sdt.IsReadOnly || (m_View?.ReadOnly ?? false);

        /// <summary>Pasa al part lo que se editó en la grilla y todavía no se grabó.</summary>
        public void Flush() => m_View?.UpdateData();

        /// <summary>Marca el documento como modificado y vuelve a cargar la grilla desde el part.</summary>
        public void Reload()
        {
            // Dirty antes de UpdateView: así el editor fuerza su propio estado "modificado"
            if (m_DocumentPart != null)
                m_DocumentPart.Dirty = true;
            m_View?.UpdateView();
            Document.Dirty = true;
        }

        /// <summary>El editor (IGxView) que muestra ese part, si ya se creó.</summary>
        static IGxView FindView(IGxDocumentPart documentPart)
        {
            if (UIServices.Environment.ActiveView is Control activeView)
            {
                IGxView inActive = FindView(activeView, documentPart);
                if (inActive != null)
                    return inActive;
            }
            Control main = UIServices.Environment.MainWindow == null ? null : Control.FromHandle(UIServices.Environment.MainWindow.Handle);
            return main == null ? null : FindView(main, documentPart);
        }

        static IGxView FindView(Control root, IGxDocumentPart documentPart)
        {
            if (root is IGxView view && ReferenceEquals(view.Viewed, documentPart))
                return view;
            foreach (Control child in root.Controls)
            {
                IGxView found = FindView(child, documentPart);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
