using Gtk;

using MonoDevelop.Core;
using MonoDevelop.Core.Gui.Dialogs;

namespace MonoDevelop.WelcomePage
{
	public class WelcomePageOptionPanel : OptionsPanel
	{
		CheckButton showOnStartCheckButton = new CheckButton();
		
		public override Widget CreatePanelWidget ()
		{
			VBox vbox = new VBox();
			showOnStartCheckButton.Label = GettextCatalog.GetString ("Show welcome page on startup");
			showOnStartCheckButton.Active = PropertyService.Get("WelcomePage.ShowOnStartup", true);
			vbox.PackStart(showOnStartCheckButton, false, false, 0);
			vbox.ShowAll ();
			return vbox;
		}
		
		public override void ApplyChanges ()
		{
			PropertyService.Set("WelcomePage.ShowOnStartup", showOnStartCheckButton.Active);
		}
	}

}
