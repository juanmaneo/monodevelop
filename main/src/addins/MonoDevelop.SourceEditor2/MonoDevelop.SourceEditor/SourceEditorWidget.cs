// SourceEditorWidget.cs
//
// Author:
//   Mike Krüger <mkrueger@novell.com>
//
// Copyright (c) 2008 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Gtk;
using Mono.TextEditor;
using MonoDevelop.Ide.Gui.Content;
using MonoDevelop.Components.Commands;
using MonoDevelop.Core;
using MonoDevelop.Projects;
using MonoDevelop.Ide.Commands;
using Document = Mono.TextEditor.TextDocument;
using Services = MonoDevelop.Projects.Services;
using System.Threading;
using MonoDevelop.Ide;
using MonoDevelop.Components;
using Mono.TextEditor.Theatrics;
using System.ComponentModel;
using ICSharpCode.NRefactory.TypeSystem;
using MonoDevelop.TypeSystem;

namespace MonoDevelop.SourceEditor
{
	
	class SourceEditorWidget : ITextEditorExtension, IQuickTaskProvider
	{
		SourceEditorView view;
		DecoratedScrolledWindow mainsw;

		// We need a reference to TextEditorData to be able to access the
		// editor document without getting the TextEditor property. This
		// property runs some gtk code, so it can only be used from the GUI thread.
		// Other threads can use textEditorData to get the document.
		TextEditorData textEditorData;
		
		const uint CHILD_PADDING = 0;
		
//		bool shouldShowclassBrowser;
//		bool canShowClassBrowser;
		ISourceEditorOptions options {
			get {
				return this.textEditor.Options;
			}
		}
		
		bool isDisposed = false;
		
		ParsedDocument parsedDocument;
		
		readonly MonoDevelop.SourceEditor.ExtensibleTextEditor textEditor;
		MonoDevelop.SourceEditor.ExtensibleTextEditor splittedTextEditor;
		MonoDevelop.SourceEditor.ExtensibleTextEditor lastActiveEditor;
		
		public MonoDevelop.SourceEditor.ExtensibleTextEditor TextEditor {
			get {
				return lastActiveEditor;
			}
		}
		
		public TextEditorContainer TextEditorContainer {
			get {
				return lastActiveEditor == textEditor ? textEditorContainer : splittedTextEditorContainer;
			}
		}
		
		public Ambience Ambience {
			get {
				string fileName = this.view.IsUntitled ? this.view.UntitledName : this.view.ContentName;
				return AmbienceService.GetAmbienceForFile (fileName);
			}
		}
		
		List<IQuickTaskProvider> quickTaskProvider = new List<IQuickTaskProvider> ();
		public void AddQuickTaskProvider (IQuickTaskProvider provider)
		{
			quickTaskProvider.Add (provider);
			mainsw.AddQuickTaskStrip (provider); 
			if (secondsw != null)
				secondsw.AddQuickTaskStrip (provider);
		}
		
		#region ITextEditorExtension
		
		ITextEditorExtension ITextEditorExtension.Next {
			get {
				return null;
			}
		}
		
		object ITextEditorExtension.GetExtensionCommandTarget ()
		{
			return null;
		}

		void ITextEditorExtension.TextChanged (int startIndex, int endIndex)
		{
		}

		void ITextEditorExtension.CursorPositionChanged ()
		{
		}

		bool ITextEditorExtension.KeyPress (Gdk.Key key, char keyChar, Gdk.ModifierType modifier)
		{
			this.TextEditor.SimulateKeyPress (key, (uint)keyChar, modifier);
			if (key == Gdk.Key.Escape)
				return true;
			return false;
		}
		#endregion
		
		public bool HasMessageBar {
			get { return messageBar != null; }
		}
		
		Gtk.VBox vbox = new Gtk.VBox ();
		public Gtk.VBox Vbox {
			get { return this.vbox; }
		}
		
		public class Border : Gtk.DrawingArea
		{
			protected override bool OnExposeEvent (Gdk.EventExpose evnt)
			{
				evnt.Window.DrawRectangle (this.Style.DarkGC (State), true, evnt.Area);
				return true;
			}
		}
		
		
		class DecoratedScrolledWindow : HBox
		{
			SourceEditorWidget parent;
			SmartScrolledWindow scrolledWindow;
			
			QuickTaskStrip strip;
			
			public Adjustment Hadjustment {
				get {
					return scrolledWindow.Hadjustment;
				}
			}
			
			public Adjustment Vadjustment {
				get {
					return scrolledWindow.Vadjustment;
				}
			}
			
			public DecoratedScrolledWindow (SourceEditorWidget parent)
			{
				this.parent = parent;
				this.strip = new QuickTaskStrip ();
				/*
				Border border = new Border ();
				border.HeightRequest = 1;
				PackStart (border, false, true, 0);
								 
				HBox box = new HBox ();
				
				border = new Border ();
				border.WidthRequest = 1;
				box.PackStart (border, false, true, 0);
				
				scrolledWindow = new ScrolledWindow ();
				scrolledWindow.BorderWidth = 0;
				scrolledWindow.ShadowType = ShadowType.None;
				scrolledWindow.ButtonPressEvent += PrepareEvent;
				box.PackStart (scrolledWindow, true, true, 0);
				
				
				border = new Border ();
				border.WidthRequest = 1;
				box.PackStart (border, false, true, 0);
				
				PackStart (box, true, true, 0);
				
				border = new Border ();
				border.HeightRequest = 1;
				PackStart (border, false, true, 0);*/
				
				scrolledWindow = new SmartScrolledWindow ();
//				scrolledWindow.BorderWidth = 0;
//				scrolledWindow.ShadowType = ShadowType.In;
				scrolledWindow.ButtonPressEvent += PrepareEvent;
				PackStart (scrolledWindow, true, true, 0);
				if (parent.quickTaskProvider.Count > 0) {
					strip.VAdjustment = scrolledWindow.Vadjustment;
					scrolledWindow.ReplaceVScrollBar (strip);
				} else {
					strip.Visible = false;
				}
				parent.quickTaskProvider.ForEach (p => AddQuickTaskStrip (p));
			}
			
			public void AddQuickTaskStrip (IQuickTaskProvider p)
			{
				if (!strip.Visible) {
					strip.VAdjustment = scrolledWindow.Vadjustment;
					scrolledWindow.ReplaceVScrollBar (strip);
				}
				p.TasksUpdated += (sender, e) => strip.Update (p);
			}
			
			protected override void OnDestroyed ()
			{
				if (scrolledWindow.Child != null)
					RemoveEvents ();
				
				scrolledWindow.ButtonPressEvent -= PrepareEvent;
				base.OnDestroyed ();
			}
			
			void PrepareEvent (object sender, ButtonPressEventArgs args) 
			{
				args.RetVal = true;
			}
		
			public void SetTextEditor (TextEditorContainer container)
			{
				scrolledWindow.Child = container;
				this.strip.TextEditor = container.TextEditorWidget;
//				container.TextEditorWidget.EditorOptionsChanged += OptionsChanged;
				container.TextEditorWidget.Caret.ModeChanged += parent.UpdateLineColOnEventHandler;
				container.TextEditorWidget.Caret.PositionChanged += parent.CaretPositionChanged;
				container.TextEditorWidget.SelectionChanged += parent.UpdateLineColOnEventHandler;
			}
			
			void OptionsChanged (object sender, EventArgs e)
			{
				TextEditor editor = (TextEditor)sender;
				scrolledWindow.ModifyBg (StateType.Normal, editor.ColorStyle.Default.BackgroundColor);
			}
			
			void RemoveEvents ()
			{
				TextEditorContainer container = scrolledWindow.Child as TextEditorContainer;
				if (container == null) {
					LoggingService.LogError ("can't remove events from text editor container.");
					return;
				}
//				container.TextEditorWidget.EditorOptionsChanged -= OptionsChanged;
				container.TextEditorWidget.Caret.ModeChanged -= parent.UpdateLineColOnEventHandler;
				container.TextEditorWidget.Caret.PositionChanged -= parent.CaretPositionChanged;
				container.TextEditorWidget.SelectionChanged -= parent.UpdateLineColOnEventHandler;
			}
			
			public TextEditorContainer RemoveTextEditor ()
			{
				TextEditorContainer child = scrolledWindow.Child as TextEditorContainer;
				if (child == null)
					return null;
				RemoveEvents ();
				scrolledWindow.Remove (child);
				child.Unparent ();
				return child;
			}
		}
		
		TextEditorContainer textEditorContainer;
		public SourceEditorWidget (SourceEditorView view)
		{
			this.view = view;
			vbox.SetSizeRequest (32, 32);
			this.lastActiveEditor = this.textEditor = new MonoDevelop.SourceEditor.ExtensibleTextEditor (view);
			this.textEditor.FocusInEvent += (o, s) => lastActiveEditor = (ExtensibleTextEditor)o;
			this.textEditor.FocusOutEvent += delegate {
				if (this.splittedTextEditor == null || !splittedTextEditor.HasFocus)
					OnLostFocus ();
			};
			mainsw = new DecoratedScrolledWindow (this);
			this.textEditorContainer = new TextEditorContainer (textEditor);
			mainsw.SetTextEditor (textEditorContainer);
			
			vbox.PackStart (mainsw, true, true, 0);
			
			textEditorData = textEditor.GetTextEditorData ();
			ResetFocusChain ();
			
			UpdateLineCol ();
			//			this.IsClassBrowserVisible = this.widget.TextEditor.Options.EnableQuickFinder;
			vbox.BorderWidth = 0;
			vbox.Spacing = 0;
			vbox.Focused += delegate {
				UpdateLineCol ();
			};
			vbox.Destroyed += delegate {
				isDisposed = true;
				RemoveErrorUndelinesResetTimerId ();
				StopParseInfoThread ();
				KillWidgets ();
				
				this.lastActiveEditor = null;
				this.splittedTextEditor = null;
				view = null;
				
				IdeApp.Workbench.StatusBar.ClearCaretState ();
				if (parseInformationUpdaterWorkerThread != null) {
					parseInformationUpdaterWorkerThread.Dispose ();
					parseInformationUpdaterWorkerThread = null;
				}
			};
			vbox.ShowAll ();
			parseInformationUpdaterWorkerThread = new BackgroundWorker ();
			parseInformationUpdaterWorkerThread.WorkerSupportsCancellation = true;
			parseInformationUpdaterWorkerThread.DoWork += HandleParseInformationUpdaterWorkerThreadDoWork;
		}

		void OnLostFocus ()
		{
			//clears search status messages
			IdeApp.Workbench.StatusBar.ShowReady ();
		}

		void UpdateLineColOnEventHandler (object sender, EventArgs e)
		{
			this.UpdateLineCol ();
		}
		
		void ResetFocusChain ()
		{
			List<Widget> focusChain = new List<Widget> ();
			
			focusChain.Add (this.textEditor);
			if (this.searchAndReplaceWidget != null) {
				focusChain.Add (this.searchAndReplaceWidget);
			}
			if (this.gotoLineNumberWidget != null) {
				focusChain.Add (this.gotoLineNumberWidget);
			}
			vbox.FocusChain = focusChain.ToArray ();
		}
		
		public void Dispose ()
		{
			RemoveErrorUndelinesResetTimerId ();
		}
		
		FoldSegment AddMarker (List<FoldSegment> foldSegments, string text, DomRegion region, FoldingType type)
		{
			Document document = textEditorData.Document;
			if (document == null || region.BeginLine <= 0 || region.EndLine <= 0 || region.BeginLine > document.LineCount || region.EndLine > document.LineCount)
				return null;
			
			int startOffset = document.LocationToOffset (region.BeginLine, region.BeginColumn);
			int endOffset   = document.LocationToOffset (region.EndLine, region.EndColumn );
			
			FoldSegment result = new FoldSegment (document, text, startOffset, endOffset - startOffset, type);
			
			foldSegments.Add (result);
			return result;
		}
		HashSet<string> symbols = new HashSet<string> ();
		bool reloadSettings;
		
		void HandleParseInformationUpdaterWorkerThreadDoWork (object sender, DoWorkEventArgs e)
		{
			BackgroundWorker worker = sender as BackgroundWorker;
			var parsedDocument = (ParsedDocument)e.Argument;
			var doc = Document;
			if (doc == null || parsedDocument == null)
				return;
			UpdateErrorUndelines (parsedDocument);
			if (!options.ShowFoldMargin)
				return;
			// don't update parsed documents that contain errors - the foldings from there may be invalid.
			if (doc.HasFoldSegments && parsedDocument.Errors.Any ())
				return;
			try {
				List<FoldSegment > foldSegments = new List<FoldSegment> ();
				bool updateSymbols = parsedDocument.Defines.Count != symbols.Count;
				if (!updateSymbols) {
					foreach (PreProcessorDefine define in parsedDocument.Defines) {
						if (!symbols.Contains (define.Define)) {
							updateSymbols = true;
							break;
						}
					}
				}
				
				if (updateSymbols) {
					symbols.Clear ();
					foreach (PreProcessorDefine define in parsedDocument.Defines) {
						symbols.Add (define.Define);
					}
				}
				
				foreach (FoldingRegion region in parsedDocument.Foldings) {
					if (worker != null && worker.CancellationPending)
						return;
					FoldingType type = FoldingType.None;
					bool setFolded = false;
					bool folded = false;
					
					//decide whether the regions should be folded by default
					switch (region.Type) {
					case FoldType.Member:
						type = FoldingType.TypeMember;
						break;
					case FoldType.Type:
						type = FoldingType.TypeDefinition;
						break;
					case FoldType.UserRegion:
						type = FoldingType.Region;
						setFolded = options.DefaultRegionsFolding;
						folded = true;
						break;
					case FoldType.Comment:
						setFolded = options.DefaultCommentFolding;
						folded = true;
						break;
					case FoldType.CommentInsideMember:
						setFolded = options.DefaultCommentFolding;
						folded = false;
						break;
					case FoldType.Undefined:
						setFolded = true;
						folded = region.IsFoldedByDefault;
						break;
					}
					
					//add the region
					FoldSegment marker = AddMarker (foldSegments, region.Name, 
					                                       region.Region, type);
					
					//and, if necessary, set its fold state
					if (marker != null && setFolded && worker == null) {
						// only fold on document open, later added folds are NOT folded by default.
						marker.IsFolded = folded;
						continue;
					}
					if (marker != null && region.Region.IsInside (textEditorData.Caret.Line, textEditorData.Caret.Column))
						marker.IsFolded = false;
					
				}
				doc.UpdateFoldSegmentWorker (sender, new DoWorkEventArgs (foldSegments));
				
				if (reloadSettings) {
					reloadSettings = false;
					Application.Invoke (delegate {
						if (isDisposed)
							return;
						view.LoadSettings ();
						mainsw.QueueDraw ();
					});
				}
			} catch (Exception ex) {
				LoggingService.LogError ("Unhandled exception in ParseInformationUpdaterWorkerThread", ex);
			}
		}
		
		BackgroundWorker parseInformationUpdaterWorkerThread;
		
		internal void UpdateParsedDocument (ParsedDocument document)
		{
			if (this.isDisposed || document == null || this.view == null)
				return;
			
			if (MonoDevelop.Core.PropertyService.Get ("EnableSemanticHighlighting", true) && TextEditor != null) {
				var margin = TextEditor.TextViewMargin;
				if (margin != null)
					Gtk.Application.Invoke (delegate { margin.PurgeLayoutCache (); });
			}
			SetParsedDocument (document, parsedDocument != null);
		}
		
		public ParsedDocument ParsedDocument {
			get {
				return this.parsedDocument;
			}
			set {
				SetParsedDocument (value, true);
			}
		}

		internal void SetParsedDocument (ParsedDocument newDocument, bool runInThread)
		{
			this.parsedDocument = newDocument;
			if (parsedDocument == null || parseInformationUpdaterWorkerThread == null)
				return;
			StopParseInfoThread ();
			if (runInThread) {
				parseInformationUpdaterWorkerThread.RunWorkerAsync (parsedDocument);
			} else {
				HandleParseInformationUpdaterWorkerThreadDoWork (null, new DoWorkEventArgs (parsedDocument));
			}
		}
		
		void StopParseInfoThread ()
		{
			if (!parseInformationUpdaterWorkerThread.IsBusy)
				return;
			parseInformationUpdaterWorkerThread.CancelAsync ();
			WaitForParseInformationUpdaterWorkerThread ();
			parseInformationUpdaterWorkerThread.Dispose ();
			
			parseInformationUpdaterWorkerThread = new BackgroundWorker ();
			parseInformationUpdaterWorkerThread.WorkerSupportsCancellation = true;
			parseInformationUpdaterWorkerThread.DoWork += HandleParseInformationUpdaterWorkerThreadDoWork;
		}
		
		public void WaitForParseInformationUpdaterWorkerThread ()
		{
			int count = 0;
			while (count++ < 5 && parseInformationUpdaterWorkerThread.IsBusy)
				Thread.Sleep (20);
		}
		
		internal void SetLastActiveEditor (ExtensibleTextEditor editor)
		{
			this.lastActiveEditor = editor;
		}
			
		#region Error underlining
		List<ErrorMarker> errors = new List<ErrorMarker> ();
		uint resetTimerId;
		
		void RemoveErrorUndelinesResetTimerId ()
		{
			if (resetTimerId > 0) {
				GLib.Source.Remove (resetTimerId);
				resetTimerId = 0;
			}
		}
		
		void UpdateErrorUndelines (ParsedDocument parsedDocument)
		{
			if (!options.UnderlineErrors || parsedDocument == null)
				return;
				
			Application.Invoke (delegate {
				if (!quickTaskProvider.Contains (this))
					AddQuickTaskProvider (this);
				RemoveErrorUndelinesResetTimerId ();
				const uint timeout = 500;
				resetTimerId = GLib.Timeout.Add (timeout, delegate {
					if (!this.isDisposed) {
						Document doc = this.TextEditor != null ? this.TextEditor.Document : null;
						if (doc != null) {
							RemoveErrorUnderlines (doc);
							
							// Else we underline the error
							if (parsedDocument.Errors != null) {
								foreach (var error in parsedDocument.Errors)
									UnderLineError (doc, error);
							}
						}
					}
					resetTimerId = 0;
					return false;
				});
				UpdateQuickTasks (parsedDocument);
			});
		}
		
		void RemoveErrorUnderlines (Document doc)
		{
			errors.ForEach (err => doc.RemoveMarker (err));
			errors.Clear ();
		}
		
		void UnderLineError (Document doc, Error info)
		{
			var line = doc.GetLine (info.Region.BeginLine);
			// If the line is already underlined
			if (errors.Any (em => em.LineSegment == line))
				return;
			ErrorMarker error = new ErrorMarker (info, line);
			errors.Add (error);
			doc.AddMarker (line, error);
		}
		#endregion
	
		
		Gtk.Paned splitContainer = null;
		public bool IsSplitted {
			get {
				return splitContainer != null;
			}
		}
		
		public bool EditorHasFocus {
			get {
				Gtk.Container c = vbox;
				while (c != null) {
					if (c.FocusChild == textEditor)
						return true;
					c = c.FocusChild as Gtk.Container;
				}
				return false;
			}
		}

		public SourceEditorView View {
			get {
				return view;
			}
			set {
				view = value;
			}
		}
		
		public void Unsplit ()
		{
			if (splitContainer == null)
				return;
			double vadjustment = mainsw.Vadjustment.Value;
			double hadjustment = mainsw.Hadjustment.Value;
			
			splitContainer.Remove (mainsw);
			secondsw.Destroy ();
			secondsw = null;
			splittedTextEditor = null;
			
			vbox.Remove (splitContainer);
			splitContainer.Destroy ();
			splitContainer = null;
			
			RecreateMainSw ();
			vbox.PackStart (mainsw, true, true, 0);
			vbox.ShowAll ();
			mainsw.Vadjustment.Value = vadjustment; 
			mainsw.Hadjustment.Value = hadjustment;
		}
		
		public void SwitchWindow ()
		{
			if (splittedTextEditor.HasFocus) {
				this.textEditor.GrabFocus ();
			} else {
				this.splittedTextEditor.GrabFocus ();
			}
		}
		DecoratedScrolledWindow secondsw;
		TextEditorContainer splittedTextEditorContainer;
		
		
		public void Split (bool vSplit)
		{
			double vadjustment = this.mainsw.Vadjustment.Value;
			double hadjustment = this.mainsw.Hadjustment.Value;
			
			if (splitContainer != null)
				Unsplit ();
			vbox.Remove (this.mainsw);
			
			RecreateMainSw ();

			this.splitContainer = vSplit ? (Gtk.Paned)new VPaned () : (Gtk.Paned)new HPaned ();

			splitContainer.Add1 (mainsw);

			this.splitContainer.ButtonPressEvent += delegate(object sender, ButtonPressEventArgs args) {
				if (args.Event.Type == Gdk.EventType.TwoButtonPress && args.RetVal == null) {
					Unsplit ();
				}
			};
			secondsw = new DecoratedScrolledWindow (this);
			this.splittedTextEditor = new MonoDevelop.SourceEditor.ExtensibleTextEditor (view, this.textEditor.Options, textEditor.Document);
			this.splittedTextEditor.FocusInEvent += (o, s) => lastActiveEditor = (ExtensibleTextEditor)o;
			this.splittedTextEditor.FocusOutEvent += delegate {
				 if (!textEditor.HasFocus)
					OnLostFocus ();
			};
			this.splittedTextEditor.Extension = textEditor.Extension;
			
			this.splittedTextEditorContainer = new TextEditorContainer (this.splittedTextEditor);
			secondsw.SetTextEditor (this.splittedTextEditorContainer);
			splitContainer.Add2 (secondsw);
			
			vbox.PackStart (splitContainer, true, true, 0);
			this.splitContainer.Position = (vSplit ? vbox.Allocation.Height : vbox.Allocation.Width) / 2 - 1;
			
			vbox.ShowAll ();
			secondsw.Vadjustment.Value = mainsw.Vadjustment.Value = vadjustment; 
			secondsw.Hadjustment.Value = mainsw.Hadjustment.Value = hadjustment;
		}

		void RecreateMainSw ()
		{
			// destroy old scrolled window to work around Bug 526721 - When splitting window vertically, 
			// the slider under left split is not shown unitl window is resized
			double vadjustment = this.mainsw.Vadjustment.Value;
			double hadjustment = this.mainsw.Hadjustment.Value;
			
			var removedTextEditor = this.mainsw.RemoveTextEditor ();
			this.mainsw.Destroy ();
			
			this.mainsw = new DecoratedScrolledWindow (this);
			this.mainsw.SetTextEditor (removedTextEditor);
			this.mainsw.Vadjustment.Value = vadjustment; 
			this.mainsw.Hadjustment.Value = hadjustment;
		}

		
//		void SplitContainerSizeRequested (object sender, SizeRequestedArgs args)
//		{
//			this.splitContainer.SizeRequested -= SplitContainerSizeRequested;
//			this.splitContainer.Position = args.Requisition.Width / 2;
//			this.splitContainer.SizeRequested += SplitContainerSizeRequested;
//		}
//		
		MonoDevelop.Components.InfoBar messageBar = null;
		
		internal static string EllipsizeMiddle (string str, int truncLen)
		{
			if (str == null) 
				return "";
			if (str.Length <= truncLen) 
				return str;
			
			string delimiter = "...";
			int leftOffset = (truncLen - delimiter.Length) / 2;
			int rightOffset = str.Length - truncLen + leftOffset + delimiter.Length;
			return str.Substring (0, leftOffset) + delimiter + str.Substring (rightOffset);
		}
		
		public void ShowFileChangedWarning ()
		{
			RemoveMessageBar ();
			
			if (messageBar == null) {
				messageBar = new MonoDevelop.Components.InfoBar (MessageType.Warning);
				messageBar.SetMessageLabel (GettextCatalog.GetString (
					"<b>The file \"{0}\" has been changed outside of MonoDevelop.</b>\n" +
					"Do you want to keep your changes, or reload the file from disk?",
					EllipsizeMiddle (Document.FileName, 50)));
				
				Button b1 = new Button (GettextCatalog.GetString("_Reload from disk"));
				b1.Image = ImageService.GetImage (Gtk.Stock.Refresh, IconSize.Button);
				b1.Clicked += new EventHandler (ClickedReload);
				messageBar.ActionArea.Add (b1);
				
				Button b2 = new Button (GettextCatalog.GetString("_Keep changes"));
				b2.Image = ImageService.GetImage (Gtk.Stock.Cancel, IconSize.Button);
				b2.Clicked += new EventHandler (ClickedIgnore);
				messageBar.ActionArea.Add (b2);
			}
			
			view.IsDirty = true;
			view.WarnOverwrite = true;
			vbox.PackStart (messageBar, false, false, CHILD_PADDING);
			vbox.ReorderChild (messageBar, 0);
			messageBar.ShowAll ();

			messageBar.QueueDraw ();
			
			view.WorkbenchWindow.ShowNotification = true;
		}
		
		#region Eol marker check
		bool useIncorrectMarkers;
		internal bool EnsureCorrectEolMarker (string fileName, string encoding)
		{
			if (useIncorrectMarkers)
				return true;
			var firstLine = Document.GetLine (1);
			if (firstLine != null && firstLine.DelimiterLength > 0) {
				string firstDelimiter = Document.GetTextAt (firstLine.EditableLength, firstLine.DelimiterLength);
				if (firstDelimiter != TextEditor.Options.DefaultEolMarker) {
					switch (DefaultSourceEditorOptions.Instance.LineEndingConversion) {
					case LineEndingConversion.Ask:
						ShowIncorretEolMarkers (fileName, encoding);
						return false;
					case LineEndingConversion.ConvertAlways:
						ConvertLineEndings ();
						return true;
					default:
						return true;
					}
				}
			}
			return true;
		}

		void ConvertLineEndings ()
		{
			string correctEol = TextEditor.Options.DefaultEolMarker;
			var newText = new System.Text.StringBuilder ();
			foreach (var line in Document.Lines) {
				newText.Append (TextEditor.GetTextAt (line.Offset, line.EditableLength));
				if (line.DelimiterLength > 0)
					newText.Append (correctEol);
			}
			TextEditor.Text = newText.ToString ();
		}

		void ShowIncorretEolMarkers (string fileName, string encoding)
		{
			RemoveMessageBar ();
			
			if (messageBar == null) {
				messageBar = new MonoDevelop.Components.InfoBar (MessageType.Warning);
				messageBar.SetMessageLabel (GettextCatalog.GetString (
					"<b>The file \"{0}\" has line endings which differ from the policy settings.</b>\n" +
					"Do you want to convert the line endings?",
					EllipsizeMiddle (Document.FileName, 50)));
				
				Button b1 = new Button (GettextCatalog.GetString("_Convert"));
				b1.Image = ImageService.GetImage (Gtk.Stock.Refresh, IconSize.Button);
				b1.Clicked += delegate(object sender, EventArgs e) {
					try {
						ConvertLineEndings ();
						view.Save (fileName, encoding);
					} finally {
						RemoveMessageBar ();
						view.WorkbenchWindow.ShowNotification = false;
					}
				};
				messageBar.ActionArea.Add (b1);
				
				Button b2 = new Button (GettextCatalog.GetString("_Keep line endings"));
				b2.Image = ImageService.GetImage (Gtk.Stock.Cancel, IconSize.Button);
				b2.Clicked += delegate(object sender, EventArgs e) {
					useIncorrectMarkers = true;
					RemoveMessageBar ();
					view.WorkbenchWindow.ShowNotification = false;
					
					view.Save (fileName, encoding);
				};
				messageBar.ActionArea.Add (b2);
			}
			
			vbox.PackStart (messageBar, false, false, CHILD_PADDING);
			vbox.ReorderChild (messageBar, 0);
			messageBar.ShowAll ();

			messageBar.QueueDraw ();
			view.WorkbenchWindow.ShowNotification = true;
		}
		#endregion
		public void ShowAutoSaveWarning (string fileName)
		{
			RemoveMessageBar ();
			TextEditor.Visible = false;
			if (messageBar == null) {
				messageBar = new MonoDevelop.Components.InfoBar (MessageType.Warning);
				messageBar.SetMessageLabel (GettextCatalog.GetString (
						"<b>An autosave file has been found for this file.</b>\n" +
						"This could mean that another instance of MonoDevelop is editing this " +
						"file, or that MonoDevelop crashed with unsaved changes.\n\n" +
					    "Do you want to use the original file, or load from the autosave file?"));
				
				Button b1 = new Button (GettextCatalog.GetString("_Use original file"));
				b1.Image = ImageService.GetImage (Gtk.Stock.Refresh, IconSize.Button);
				b1.Clicked += delegate {
					try {
						AutoSave.RemoveAutoSaveFile (fileName);
						TextEditor.GrabFocus ();
						view.Load (fileName);
						view.WorkbenchWindow.Document.ReparseDocument ();
					} catch (Exception ex) {
						MessageService.ShowException (ex, "Could not remove the autosave file.");
					} finally {
						RemoveMessageBar ();
					}
				};
				messageBar.ActionArea.Add (b1);
				
				Button b2 = new Button (GettextCatalog.GetString("_Load from autosave"));
				b2.Image = ImageService.GetImage (Gtk.Stock.RevertToSaved, IconSize.Button);
				b2.Clicked += delegate {
					try {
						string content = AutoSave.LoadAutoSave (fileName);
						AutoSave.RemoveAutoSaveFile (fileName);
						TextEditor.GrabFocus ();
						view.Load (fileName, content, null);
						view.WorkbenchWindow.Document.ReparseDocument ();
						view.IsDirty = true;
					} catch (Exception ex) {
						MessageService.ShowException (ex, "Could not remove the autosave file.");
					} finally {
						RemoveMessageBar ();
					}
					
				};
				messageBar.ActionArea.Add (b2);
			}
			
			view.IsDirty = true;
			view.WarnOverwrite = true;
			vbox.PackStart (messageBar, false, false, CHILD_PADDING);
			vbox.ReorderChild (messageBar, 0);
			messageBar.ShowAll ();

			messageBar.QueueDraw ();
			
//			view.WorkbenchWindow.ShowNotification = true;
		}
		
		
		public void RemoveMessageBar ()
		{
			if (messageBar != null) {
				if (messageBar.Parent == vbox)
					vbox.Remove (messageBar);
				messageBar.Destroy ();
				messageBar = null;
			}
			if (!TextEditor.Visible)
				TextEditor.Visible = true;
		}
		
		void ClickedReload (object sender, EventArgs args)
		{
			Reload ();
			view.TextEditor.GrabFocus ();
		}
		
		public void Reload ()
		{
			try {
				view.StoreSettings ();
				reloadSettings = true;
				view.Load (view.ContentName);
				view.WorkbenchWindow.ShowNotification = false;
			} catch (Exception ex) {
				MessageService.ShowException (ex, "Could not reload the file.");
			} finally {
				RemoveMessageBar ();
			}
		}
		
		void ClickedIgnore (object sender, EventArgs args)
		{
			RemoveMessageBar ();
			view.WorkbenchWindow.ShowNotification = false;
		}
		
		#region Status Bar Handling
		MonoDevelop.SourceEditor.MessageBubbleTextMarker oldExpandedMarker;
		void CaretPositionChanged (object o, DocumentLocationEventArgs args)
		{
			UpdateLineCol ();
			LineSegment curLine = TextEditor.Document.GetLine (TextEditor.Caret.Line);
			MonoDevelop.SourceEditor.MessageBubbleTextMarker marker = null;
			if (curLine != null && curLine.Markers.Any (m => m is MonoDevelop.SourceEditor.MessageBubbleTextMarker)) {
				marker = (MonoDevelop.SourceEditor.MessageBubbleTextMarker)curLine.Markers.First (m => m is MonoDevelop.SourceEditor.MessageBubbleTextMarker);
//				marker.CollapseExtendedErrors = false;
				if (oldExpandedMarker == null)
					Document.CommitLineToEndUpdate (Document.OffsetToLineNumber (curLine.Offset));
			}
			
			if (oldExpandedMarker != null && oldExpandedMarker != marker) {
//				oldExpandedMarker.CollapseExtendedErrors = true;
				int markerOffset = marker != null && marker.LineSegment != null ? marker.LineSegment.Offset : Int32.MaxValue;
				int oldMarkerOffset = oldExpandedMarker.LineSegment != null ? oldExpandedMarker.LineSegment.Offset : Int32.MaxValue;
				Document.CommitLineToEndUpdate (Document.OffsetToLineNumber (Math.Min (markerOffset, oldMarkerOffset)));
			}
			oldExpandedMarker = marker;
		}
		
//		void OnChanged (object o, EventArgs e)
//		{
//			UpdateLineCol ();
//			OnContentChanged (null);
//			needsUpdate = true;
//		}
		
		internal void UpdateLineCol ()
		{
			int offset = TextEditor.Caret.Offset;
			if (offset < 0 || offset > TextEditor.Document.Length)
				return;
			DocumentLocation location = TextEditor.LogicalToVisualLocation (TextEditor.Caret.Location);
			IdeApp.Workbench.StatusBar.ShowCaretState (TextEditor.Caret.Line,
			                                           location.Column,
			                                           TextEditor.IsSomethingSelected ? TextEditor.SelectionRange.Length : 0,
			                                           TextEditor.Caret.IsInInsertMode);
		}
		
		#endregion
		
		#region Search and Replace
		Components.RoundedFrame searchAndReplaceWidgetFrame = null;
		SearchAndReplaceWidget searchAndReplaceWidget = null;
		Components.RoundedFrame gotoLineNumberWidgetFrame = null;
		GotoLineNumberWidget   gotoLineNumberWidget   = null;
		
		public void SetSearchPattern ()
		{
			string selectedText = FormatPatternToSelectionOption (this.TextEditor.SelectedText);
			
			if (!String.IsNullOrEmpty (selectedText)) {
				this.SetSearchPattern (selectedText);
				SearchAndReplaceWidget.searchPattern = selectedText;
				SearchAndReplaceWidget.UpdateSearchHistory (selectedText);
				TextEditor.TextViewMargin.MainSearchResult = TextEditor.SelectionRange;
			}
		}
		
		bool KillWidgets ()
		{
			bool result = false;
			if (searchAndReplaceWidgetFrame != null) {
				searchAndReplaceWidgetFrame.Destroy ();
				searchAndReplaceWidgetFrame = null;
				searchAndReplaceWidget = null;
				result = true;
				//clears any message it may have set
				IdeApp.Workbench.StatusBar.ShowReady ();
			}
			
			if (gotoLineNumberWidgetFrame != null) {
				gotoLineNumberWidgetFrame.Destroy ();
				gotoLineNumberWidgetFrame = null;
				gotoLineNumberWidget = null;
				result = true;
			}
			
			if (this.textEditor != null) 
				this.textEditor.HighlightSearchPattern = false;
			if (this.splittedTextEditor != null) 
				this.splittedTextEditor.HighlightSearchPattern = false;
			
			if (!isDisposed)
				ResetFocusChain ();
			return result;
		}
		
		public void SetSearchPattern (string searchPattern)
		{
			this.textEditor.SearchPattern = searchPattern;
			if (this.splittedTextEditor != null)
				this.splittedTextEditor.SearchPattern = searchPattern;
		}
		
		public bool DisableAutomaticSearchPatternCaseMatch {
			get;
			set;
		}
		
		internal void CheckSearchPatternCasing (string searchPattern)
		{
			if (!DisableAutomaticSearchPatternCaseMatch && PropertyService.Get ("AutoSetPatternCasing", true)) {
				searchAndReplaceWidget.IsCaseSensitive = searchPattern.Any (ch => Char.IsUpper (ch));
				SetSearchOptions ();
			}
		}
		
		internal bool RemoveSearchWidget ()
		{
			bool result = KillWidgets ();
			if (!isDisposed)
				TextEditor.GrabFocus ();
			return result;
		}
		
		public void EmacsFindNext ()
		{
			if (searchAndReplaceWidget == null) {
				ShowSearchWidget ();
			} else {
				this.FindNext ();
			}
		}
		
		public void EmacsFindPrevious ()
		{
			if (searchAndReplaceWidget == null) {
				ShowSearchWidget ();
			} else {
				this.FindPrevious ();
			}
		}
		
		public void ShowSearchWidget ()
		{
			ShowSearchReplaceWidget (false);
		}
		
		public void ShowReplaceWidget ()
		{
			ShowSearchReplaceWidget (true);
		}
		
		internal void OnUpdateUseSelectionForFind (CommandInfo info)
		{
			info.Enabled = searchAndReplaceWidget != null && TextEditor.IsSomethingSelected;
		}
		
		public void UseSelectionForFind ()
		{
			SetSearchPatternToSelection ();
		}
		
		internal void OnUpdateUseSelectionForReplace (CommandInfo info)
		{
			info.Enabled = searchAndReplaceWidget != null && TextEditor.IsSomethingSelected;
		}
		
		public void UseSelectionForReplace ()
		{
			SetReplacePatternToSelection ();
		}
		
		
		void ShowSearchReplaceWidget (bool replace)
		{
			if (searchAndReplaceWidget == null) {
				this.textEditor.SearchPattern = SearchAndReplaceWidget.searchPattern = "";
				// reset pattern, to force an update
				
				if (TextEditor.IsSomethingSelected) {
					SetSearchPattern ();
				}
				
				KillWidgets ();
				searchAndReplaceWidgetFrame = new MonoDevelop.Components.RoundedFrame ();
				//searchAndReplaceWidgetFrame.SetFillColor (MonoDevelop.Components.CairoExtensions.GdkColorToCairoColor (widget.TextEditor.ColorStyle.Default.BackgroundColor));
				searchAndReplaceWidgetFrame.SetFillColor (MonoDevelop.Components.CairoExtensions.GdkColorToCairoColor (vbox.Style.Background (StateType.Normal)));
				
				searchAndReplaceWidgetFrame.Child = searchAndReplaceWidget = new SearchAndReplaceWidget (this, searchAndReplaceWidgetFrame);
				
				searchAndReplaceWidgetFrame.ShowAll ();
				this.TextEditorContainer.AddAnimatedWidget (searchAndReplaceWidgetFrame, 300, Mono.TextEditor.Theatrics.Easing.ExponentialInOut, Mono.TextEditor.Theatrics.Blocking.Downstage, this.TextEditor.Allocation.Width - 400, -searchAndReplaceWidget.Allocation.Height);
//				this.PackEnd (searchAndReplaceWidget);
//				this.SetChildPacking (searchAndReplaceWidget, false, false, CHILD_PADDING, PackType.End);
		//		searchAndReplaceWidget.ShowAll ();
				this.textEditor.HighlightSearchPattern = true;
				this.textEditor.TextViewMargin.RefreshSearchMarker ();
				if (this.splittedTextEditor != null) {
					this.splittedTextEditor.HighlightSearchPattern = true;
					this.splittedTextEditor.TextViewMargin.RefreshSearchMarker ();
				}
				
				ResetFocusChain ();
			} else {
				if (TextEditor.IsSomethingSelected) {
					SetSearchPattern ();
				}
			}
			searchAndReplaceWidget.UpdateSearchPattern ();
			searchAndReplaceWidget.IsReplaceMode = replace;
			if (searchAndReplaceWidget.SearchFocused) {
				if (replace) {
					this.Replace ();
				} else {
					this.FindNext ();
				}
			}
			searchAndReplaceWidget.Focus ();
			SetSearchOptions ();
		}
		
		public void ShowGotoLineNumberWidget ()
		{
			if (gotoLineNumberWidget == null) {
				KillWidgets ();
				
				
				gotoLineNumberWidgetFrame = new MonoDevelop.Components.RoundedFrame ();
				//searchAndReplaceWidgetFrame.SetFillColor (MonoDevelop.Components.CairoExtensions.GdkColorToCairoColor (widget.TextEditor.ColorStyle.Default.BackgroundColor));
				gotoLineNumberWidgetFrame.SetFillColor (MonoDevelop.Components.CairoExtensions.GdkColorToCairoColor (vbox.Style.Background (StateType.Normal)));
				
				gotoLineNumberWidgetFrame.Child = gotoLineNumberWidget = new GotoLineNumberWidget (this, gotoLineNumberWidgetFrame);
				gotoLineNumberWidgetFrame.ShowAll ();
				
				this.TextEditorContainer.AddAnimatedWidget (gotoLineNumberWidgetFrame, 300, Mono.TextEditor.Theatrics.Easing.ExponentialInOut, Mono.TextEditor.Theatrics.Blocking.Downstage, this.TextEditor.Allocation.Width - 400, -gotoLineNumberWidget.Allocation.Height);
				
				ResetFocusChain ();
			}
			
			gotoLineNumberWidget.Focus ();
		}
		
		internal void SetSearchOptions ()
		{
			if (SearchAndReplaceWidget.SearchEngine == SearchAndReplaceWidget.DefaultSearchEngine) {
				if (!(this.textEditor.SearchEngine is BasicSearchEngine))
					this.textEditor.SearchEngine = new BasicSearchEngine ();
			} else {
				if (!(this.textEditor.SearchEngine is RegexSearchEngine))
					this.textEditor.SearchEngine = new RegexSearchEngine ();
			}
			if (searchAndReplaceWidget != null) 
				this.textEditor.IsCaseSensitive = searchAndReplaceWidget.IsCaseSensitive;
			this.textEditor.IsWholeWordOnly = SearchAndReplaceWidget.IsWholeWordOnly;
			
			string error;
			string pattern = SearchAndReplaceWidget.searchPattern;
			if (searchAndReplaceWidget != null)
				pattern = searchAndReplaceWidget.SearchPattern;
			
			bool valid = this.textEditor.SearchEngine.IsValidPattern (pattern, out error);
			
			if (valid) {
				this.textEditor.SearchPattern = pattern;
			}
			this.textEditor.QueueDraw ();
			if (this.splittedTextEditor != null) {
				if (searchAndReplaceWidget != null)
					this.splittedTextEditor.IsCaseSensitive = searchAndReplaceWidget.IsCaseSensitive;
				this.splittedTextEditor.IsWholeWordOnly = SearchAndReplaceWidget.IsWholeWordOnly;
				if (valid) {
					this.splittedTextEditor.SearchPattern = pattern;
				}
				this.splittedTextEditor.QueueDraw ();
			}
		}
		
		public SearchResult FindNext ()
		{
			return FindNext (true);
		}
		
		public SearchResult FindNext (bool focus)
		{
			SetSearchOptions ();
			SearchResult result = TextEditor.FindNext (true);
			if (focus) {
				TextEditor.GrabFocus ();
			}
			if (searchAndReplaceWidget != null)
				TextEditor.CenterToCaret ();
			if (result == null) {
				IdeApp.Workbench.StatusBar.ShowError (GettextCatalog.GetString ("Search pattern not found"));
			} else if (result.SearchWrapped) {
				IdeApp.Workbench.StatusBar.ShowMessage (new Image (Gtk.Stock.Find, IconSize.Menu),
					GettextCatalog.GetString ("Reached bottom, continued from top"));
			} else {
				IdeApp.Workbench.StatusBar.ShowReady ();
			}
			return result;
		}
		
		public SearchResult FindPrevious ()
		{
			return FindPrevious (true);
		}
		
		public SearchResult FindPrevious (bool focus)
		{
			SetSearchOptions ();
			SearchResult result = TextEditor.FindPrevious (true);
			if (focus) {
				TextEditor.GrabFocus ();
			}
			if (searchAndReplaceWidget != null)
				TextEditor.CenterToCaret ();
			if (result == null) {
				IdeApp.Workbench.StatusBar.ShowError (GettextCatalog.GetString ("Search pattern not found"));
			} else if (result.SearchWrapped) {
				IdeApp.Workbench.StatusBar.ShowMessage (
					new Image (Gtk.Stock.Find, IconSize.Menu),
					GettextCatalog.GetString ("Reached top, continued from bottom"));
			} else {
				IdeApp.Workbench.StatusBar.ShowReady ();
			}
			return result;
		}

		string FormatPatternToSelectionOption (string pattern)
		{
			return MonoDevelop.Ide.FindInFiles.FindInFilesDialog.FormatPatternToSelectionOption (pattern, SearchAndReplaceWidget.SearchEngine == SearchAndReplaceWidget.RegexSearchEngine);
		}
		
		void SetSearchPatternToSelection ()
		{
			SetSearchPattern ();
			if (TextEditor.IsSomethingSelected) {
				var pattern = FormatPatternToSelectionOption (TextEditor.SelectedText);
					
				TextEditor.SearchPattern = pattern;
				SearchAndReplaceWidget.UpdateSearchHistory (TextEditor.SearchPattern);
			}
			if (searchAndReplaceWidget != null)
				searchAndReplaceWidget.UpdateSearchPattern ();
		}
		
		void SetReplacePatternToSelection ()
		{
			if (searchAndReplaceWidget != null && TextEditor.IsSomethingSelected)
				searchAndReplaceWidget.ReplacePattern = TextEditor.SelectedText;
		}
		
		public SearchResult FindNextSelection ()
		{
			SetSearchPatternToSelection ();
			
			SetSearchOptions ();
			SetSearchPattern();
			TextEditor.GrabFocus ();
			return FindNext ();
		}
	
		public SearchResult FindPreviousSelection ()
		{
			SetSearchPatternToSelection ();
			SetSearchOptions ();
			SetSearchPattern();
			TextEditor.GrabFocus ();
			return FindPrevious ();
		}
		
		public void Replace ()
		{
			SetSearchOptions ();
			TextEditor.Replace (searchAndReplaceWidget.ReplacePattern);
			TextEditor.GrabFocus ();
		}
		
		public void ReplaceAll ()
		{
			SetSearchOptions ();
			int number = TextEditor.ReplaceAll (searchAndReplaceWidget.ReplacePattern);
			if (number == 0) {
				IdeApp.Workbench.StatusBar.ShowError (GettextCatalog.GetString ("Search pattern not found"));
			} else {
				IdeApp.Workbench.StatusBar.ShowMessage (
					GettextCatalog.GetPluralString ("Found and replaced one occurrence",
					                                "Found and replaced {0} occurrences", number, number));
			}
			TextEditor.GrabFocus ();
		}
		#endregion
	
		public Mono.TextEditor.TextDocument Document {
			get {
				var editor = TextEditor;
				if (editor == null)
					return null;
				return editor.Document;
			}
		}
		
		#region Help
		internal void MonodocResolver ()
		{
			DomRegion region;
			var res = TextEditor.GetLanguageItem (TextEditor.Caret.Offset, out region);
			string url = HelpService.GetMonoDocHelpUrl (res);
			if (url != null)
				IdeApp.HelpOperations.ShowHelp (url);
		}
		
		internal void MonodocResolverUpdate (CommandInfo cinfo)
		{
			DomRegion region;
			var res = TextEditor.GetLanguageItem (TextEditor.Caret.Offset, out region);
			if (res == null || !IdeApp.HelpOperations.CanShowHelp (res))
				cinfo.Bypass = true;
		}
		
		#endregion
		
		#region commenting and indentation
		internal void OnUpdateToggleComment (MonoDevelop.Components.Commands.CommandInfo info)
		{
//			List<string> lineComments;
//			if (Document.SyntaxMode.Properties.TryGetValue ("LineComment", out lineComments)) {
//				info.Visible = lineComments.Count > 0;
//			} else {
//				List<string> blockStarts;
//				List<string> blockEnds;
//				if (Document.SyntaxMode.Properties.TryGetValue ("BlockCommentStart", out blockStarts) && Document.SyntaxMode.Properties.TryGetValue ("BlockCommentEnd", out blockEnds)) {
//					info.Visible = blockStarts.Count > 0 && blockEnds.Count > 0;
//				}
//			}
		}
		
		void ToggleCodeCommentWithBlockComments ()
		{

//			List<string> blockStarts;
//			if (!Document.SyntaxMode.Properties.TryGetValue ("BlockCommentStart", out blockStarts) || blockStarts.Count == 0)
//				return;
//
//			List<string> blockEnds;
//			if (!Document.SyntaxMode.Properties.TryGetValue ("BlockCommentEnd", out blockEnds) || blockEnds.Count == 0)
//				return;
//
//			string blockStart = blockStarts[0];
//			string blockEnd = blockEnds[0];
//
//			using (var undo = Document.OpenUndoGroup ()) {
//				LineSegment startLine;
//				LineSegment endLine;
//	
//				if (TextEditor.IsSomethingSelected) {
//					startLine = Document.GetLineByOffset (textEditor.SelectionRange.Offset);
//					endLine = Document.GetLineByOffset (textEditor.SelectionRange.EndOffset);
//				} else {
//					startLine = endLine = Document.GetLine (textEditor.Caret.Line);
//				}
//				string startLineText = Document.GetTextAt (startLine.Offset, startLine.EditableLength);
//				string endLineText = Document.GetTextAt (endLine.Offset, endLine.EditableLength);
//				if (startLineText.StartsWith (blockStart) && endLineText.EndsWith (blockEnd)) {
//					textEditor.Remove (endLine.Offset + endLine.EditableLength - blockEnd.Length, blockEnd.Length);
//					textEditor.Remove (startLine.Offset, blockStart.Length);
//					if (TextEditor.IsSomethingSelected) {
//						TextEditor.SelectionAnchor -= blockEnd.Length;
//					}
//				} else {
//					textEditor.Insert (endLine.Offset + endLine.EditableLength, blockEnd);
//					textEditor.Insert (startLine.Offset, blockStart);
//					if (TextEditor.IsSomethingSelected) {
//						TextEditor.SelectionAnchor += blockEnd.Length;
//					}
//					
//				}
//			}
		}
		
		public void ToggleCodeComment ()
		{
//			bool comment = false;
//			List<string> lineComments;
//			if (!Document.SyntaxMode.Properties.TryGetValue ("LineComment", out lineComments) || lineComments.Count == 0) {
//				ToggleCodeCommentWithBlockComments ();
//				return;
//			}
//			string commentTag = lineComments[0];
//			
//			foreach (LineSegment line in this.textEditor.SelectedLines) {
//				string text = Document.GetTextAt (line);
//				string trimmedText = text.TrimStart ();
//				if (!trimmedText.StartsWith (commentTag)) {
//					comment = true;
//					break;
//				}
//			}
//			if (comment) {
//				CommentSelectedLines (commentTag);
//			} else {
//				UncommentSelectedLines (commentTag);
//			}
		}
		
		public void OnUpdateToggleErrorTextMarker (CommandInfo info)
		{
			LineSegment line = TextEditor.Document.GetLine (TextEditor.Caret.Line);
			if (line == null) {
				info.Visible = false;
				return;
			}
			var marker = (MessageBubbleTextMarker)line.Markers.FirstOrDefault (m => m is MessageBubbleTextMarker);
			info.Visible = marker != null;
		}
		
		public void OnToggleErrorTextMarker ()
		{
			LineSegment line = TextEditor.Document.GetLine (TextEditor.Caret.Line);
			if (line == null)
				return;
			var marker = (MessageBubbleTextMarker)line.Markers.FirstOrDefault (m => m is MessageBubbleTextMarker);
			if (marker != null) {
				marker.IsVisible = !marker.IsVisible;
				TextEditor.QueueDraw ();
				MonoDevelop.Ide.Gui.Pads.ErrorListPad pad = IdeApp.Workbench.GetPad<MonoDevelop.Ide.Gui.Pads.ErrorListPad> ().Content as MonoDevelop.Ide.Gui.Pads.ErrorListPad;
				pad.Control.QueueDraw ();
			}
		}
		
		void CommentSelectedLines (string commentTag)
		{
			int startLineNr = TextEditor.IsSomethingSelected ? Document.OffsetToLineNumber (TextEditor.SelectionRange.Offset) : TextEditor.Caret.Line;
			int endLineNr   = TextEditor.IsSomethingSelected ? Document.OffsetToLineNumber (TextEditor.SelectionRange.EndOffset) : TextEditor.Caret.Line;
			if (endLineNr < 0)
				endLineNr = Document.LineCount;
			
			LineSegment anchorLine   = TextEditor.IsSomethingSelected ? TextEditor.Document.GetLineByOffset (TextEditor.SelectionAnchor) : null;
			int         anchorColumn = TextEditor.IsSomethingSelected ? TextEditor.SelectionAnchor - anchorLine.Offset : -1;
			
			using (var undo = Document.OpenUndoGroup ()) {
				foreach (LineSegment line in TextEditor.SelectedLines) {
					TextEditor.Insert (line.Offset, commentTag);
				}
				if (TextEditor.IsSomethingSelected) {
					if (TextEditor.SelectionAnchor < TextEditor.Caret.Offset) {
						if (anchorColumn != 0) 
							TextEditor.SelectionAnchor = System.Math.Min (anchorLine.Offset + anchorLine.EditableLength, System.Math.Max (anchorLine.Offset, TextEditor.SelectionAnchor + commentTag.Length));
					} else {
						if (anchorColumn != 0) {
							TextEditor.SelectionAnchor = System.Math.Min (anchorLine.Offset + anchorLine.EditableLength, System.Math.Max (anchorLine.Offset, anchorLine.Offset + anchorColumn + commentTag.Length));
						} else {
	//						TextEditor.SelectionAnchor = anchorLine.Offset;
						}
					}
				}
				
				if (TextEditor.Caret.Column != 0) {
					TextEditor.Caret.PreserveSelection = true;
					TextEditor.Caret.Column += commentTag.Length;
					TextEditor.Caret.PreserveSelection = false;
				}
				
				if (TextEditor.IsSomethingSelected) 
					TextEditor.ExtendSelectionTo (TextEditor.Caret.Offset);
			}
			Document.CommitMultipleLineUpdate (startLineNr, endLineNr);
		}
		
		void UncommentSelectedLines (string commentTag)
		{
			int startLineNr = TextEditor.IsSomethingSelected ? Document.OffsetToLineNumber (TextEditor.SelectionRange.Offset) : TextEditor.Caret.Line;
			int endLineNr   = TextEditor.IsSomethingSelected ? Document.OffsetToLineNumber (TextEditor.SelectionRange.EndOffset) : TextEditor.Caret.Line;
			if (endLineNr < 0)
				endLineNr = Document.LineCount;
			LineSegment anchorLine   = TextEditor.IsSomethingSelected ? TextEditor.Document.GetLineByOffset (TextEditor.SelectionAnchor) : null;
			int         anchorColumn = TextEditor.IsSomethingSelected ? TextEditor.SelectionAnchor - anchorLine.Offset : -1;
			
			using (var undo = Document.OpenUndoGroup ()) {
				int first = -1;
				int last  = 0;
				foreach (LineSegment line in TextEditor.SelectedLines) {
					string text = Document.GetTextAt (line);
					string trimmedText = text.TrimStart ();
					int length = 0;
					if (trimmedText.StartsWith (commentTag)) {
						TextEditor.Remove (line.Offset + (text.Length - trimmedText.Length), commentTag.Length);
						length = commentTag.Length;
					}
					last = length;
					if (first < 0)
						first = last;
				}
				
				if (TextEditor.IsSomethingSelected) {
					if (TextEditor.SelectionAnchor < TextEditor.Caret.Offset) {
						TextEditor.SelectionAnchor = System.Math.Min (anchorLine.Offset + anchorLine.EditableLength, System.Math.Max (anchorLine.Offset, TextEditor.SelectionAnchor - first));
					} else {
						TextEditor.SelectionAnchor = System.Math.Min (anchorLine.Offset + anchorLine.EditableLength, System.Math.Max (anchorLine.Offset, anchorLine.Offset + anchorColumn - last));
					}
				}
				
				if (TextEditor.Caret.Column != DocumentLocation.MinColumn) {
					TextEditor.Caret.PreserveSelection = true;
					TextEditor.Caret.Column = System.Math.Max (DocumentLocation.MinColumn, TextEditor.Caret.Column - last);
					TextEditor.Caret.PreserveSelection = false;
				}
				
				if (TextEditor.IsSomethingSelected) 
					TextEditor.ExtendSelectionTo (TextEditor.Caret.Offset);
			}
			Document.CommitMultipleLineUpdate (startLineNr, endLineNr);
		}
		
		#endregion
		
		#region IQuickTaskProvider implementation
		List<QuickTask> tasks = new List<QuickTask> ();

		public event EventHandler TasksUpdated;

		protected virtual void OnTasksUpdated (EventArgs e)
		{
			EventHandler handler = this.TasksUpdated;
			if (handler != null)
				handler (this, e);
		}
		
		public IEnumerable<QuickTask> QuickTasks {
			get {
				return tasks;
			}
		}
		
		void UpdateQuickTasks (ParsedDocument doc)
		{
			tasks.Clear ();
			
			foreach (var cmt in doc.TagComments) {
				var newTask = new QuickTask (cmt.Text, cmt.Region.Begin, QuickTaskSeverity.Hint);
				tasks.Add (newTask);
			}
			
			foreach (var error in doc.Errors) {
				var newTask = new QuickTask (error.Message, error.Region.Begin, error.ErrorType == ErrorType.Error ? QuickTaskSeverity.Error : QuickTaskSeverity.Warning);
				tasks.Add (newTask);
			}
			
			OnTasksUpdated (EventArgs.Empty);
		}
		#endregion
	
	}

	class ErrorMarker : UnderlineMarker
	{
		public Error Info { get; private set; }
		
		public ErrorMarker (Error info, LineSegment line)
		{
			this.Info = info;
			this.LineSegment = line; // may be null if no line is assigned to the error.
			this.Wave = true;
			
			ColorName = info.ErrorType == ErrorType.Warning ? Mono.TextEditor.Highlighting.ColorSheme.WarningUnderlineString : Mono.TextEditor.Highlighting.ColorSheme.ErrorUnderlineString;
			
			if (Info.Region.BeginLine == info.Region.EndLine) {
				this.StartCol = Info.Region.BeginColumn;
				this.EndCol = Info.Region.EndColumn;
			} else {
				this.StartCol = this.EndCol = 0;
			}
		}
	}
}