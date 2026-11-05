using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.AddIn;
using Siemens.Engineering.AddIn.Menu;

namespace TiaSbom.AddIn
{
    // TODO(V21): the Add-In API (provider base class, menu types, delegate signatures) is not covered by the
    // V21 Openness manual we checked. Names below follow the V17–V20 Add-In API — verify against the V21
    // Add-In documentation / IntelliSense and adjust. Business logic is in SbomExport and is unaffected.

    /// <summary>Registered by TIA Portal; adds "SBOM" to the project tree context menu.</summary>
    public sealed class AddInProvider : ProjectTreeAddInProvider
    {
        private readonly TiaPortal _tiaPortal;

        public AddInProvider(TiaPortal tiaPortal)
        {
            _tiaPortal = tiaPortal;
        }

        protected override IEnumerable<ContextMenuAddIn> GetContextMenuAddIns()
        {
            yield return new SbomContextMenu(_tiaPortal);
        }
    }

    // Publisher requires every AddIn-derived type to be public.
    public sealed class SbomContextMenu : ContextMenuAddIn
    {
        private readonly TiaPortal _tiaPortal;

        public SbomContextMenu(TiaPortal tiaPortal) : base("SBOM")
        {
            _tiaPortal = tiaPortal;
        }

        protected override void BuildContextMenuItems(ContextMenuAddInRoot addInRootSubmenu)
        {
            addInRootSubmenu.Items.AddActionItem<IEngineeringObject>(
                "Export SBOM to Gitea", OnExport, _ => MenuStatus.Enabled);
        }

        private void OnExport(MenuSelectionProvider<IEngineeringObject> selection)
        {
            new SbomExport(_tiaPortal).Run();
        }
    }
}
