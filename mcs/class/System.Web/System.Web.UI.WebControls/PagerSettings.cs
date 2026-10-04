//
// System.Web.UI.WebControls.PagerSettings.cs
//
// Authors:
//	Lluis Sanchez Gual (lluis@novell.com)
//
// (C) 2005 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// Copyright (C) 2004 Novell, Inc (http://www.novell.com)
//


using System.ComponentModel;
using System.Security.Permissions;

namespace System.Web.UI.WebControls
{
	[TypeConverterAttribute (typeof(ExpandableObjectConverter))]
	[AspNetHostingPermissionAttribute (SecurityAction.LinkDemand, Level = AspNetHostingPermissionLevel.Minimal)]
	public sealed class PagerSettings: IStateManager
	{
		static readonly object propertyChangedEvent = new object ();
		
		StateBag ViewState = new StateBag ();
		Control ctrl;
		EventHandlerList events = new EventHandlerList ();

		[Browsable (false)]
		public event EventHandler PropertyChanged {
			add { events.AddHandler (propertyChangedEvent, value); }
			remove { events.RemoveHandler (propertyChangedEvent, value); }
		}
		
		public PagerSettings ()
		{
		}
		
		internal PagerSettings (Control ctrl)
		{
			this.ctrl = ctrl;
		}

		[WebCategoryAttribute ("Appearance")]
		[NotifyParentPropertyAttribute (true)]
		[UrlPropertyAttribute]
		[DefaultValueAttribute ("")]
		[EditorAttribute ("System.Web.UI.Design.ImageUrlEditor, " + Consts.AssemblySystem_Design, "System.Drawing.Design.UITypeEditor, " + Consts.AssemblySystem_Drawing)]
		public string FirstPageImageUrl {
			get {
				var ob = ViewState ["FirstPageImageUrl"];
				if (ob != null) return (string) ob;
				return string.Empty;
			}
			set {
				ViewState ["FirstPageImageUrl"] = value;
				RaisePropertyChanged ();
			}
		}
	
		[WebCategoryAttribute ("Appearance")]
		[DefaultValueAttribute ("&lt;&lt;")]
		[NotifyParentPropertyAttribute (true)]
		public string FirstPageText {
			get {
				var ob = ViewState ["FirstPageText"];
				if (ob != null) return (string) ob;
				return "&lt;&lt;";
			}
			set {
				ViewState ["FirstPageText"] = value;
				RaisePropertyChanged ();
			}
		}
	
		[WebCategoryAttribute ("Appearance")]
		[NotifyParentPropertyAttribute (true)]
		[UrlPropertyAttribute]
		[DefaultValueAttribute ("")]
		[EditorAttribute ("System.Web.UI.Design.ImageUrlEditor, " + Consts.AssemblySystem_Design, "System.Drawing.Design.UITypeEditor, " + Consts.AssemblySystem_Drawing)]
		public string LastPageImageUrl {
			get {
				var ob = ViewState ["LastPageImageUrl"];
				if (ob != null) return (string) ob;
				return string.Empty;
			}
			set {
				ViewState ["LastPageImageUrl"] = value;
				RaisePropertyChanged ();
			}
		}
	
		[NotifyParentPropertyAttribute (true)]
		[WebCategoryAttribute ("Appearance")]
		[DefaultValueAttribute ("&gt;&gt;")]
		public string LastPageText {
			get {
				var ob = ViewState ["LastPageText"];
				if (ob != null) return (string) ob;
				return "&gt;&gt;";
			}
			set {
				ViewState ["LastPageText"] = value;
				RaisePropertyChanged ();
			}
		}
	
		[NotifyParentPropertyAttribute (true)]
		[WebCategoryAttribute ("Appearance")]
		[DefaultValueAttribute (PagerButtons.Numeric)]
		public PagerButtons Mode {
			get {
				var ob = ViewState ["Mode"];
				if (ob != null) return (PagerButtons) ob;
				return PagerButtons.Numeric;
			}
			set {
				ViewState ["Mode"] = value;
				RaisePropertyChanged ();
			}
		}
	
		[WebCategoryAttribute ("Appearance")]
		[NotifyParentPropertyAttribute (true)]
		[UrlPropertyAttribute]
		[DefaultValueAttribute ("")]
		[EditorAttribute ("System.Web.UI.Design.ImageUrlEditor, " + Consts.AssemblySystem_Design, "System.Drawing.Design.UITypeEditor, " + Consts.AssemblySystem_Drawing)]
		public string NextPageImageUrl {
			get {
				var ob = ViewState ["NextPageImageUrl"];
				if (ob != null) return (string) ob;
				return string.Empty;
			}
			set {
				ViewState ["NextPageImageUrl"] = value;
				RaisePropertyChanged ();
			}
		}

		[WebCategoryAttribute ("Appearance")]
		[NotifyParentPropertyAttribute (true)]
		[DefaultValueAttribute ("&gt;")]
		public string NextPageText {
			get {
				var ob = ViewState ["NextPageText"];
				if (ob != null) return (string) ob;
				return "&gt;";
			}
			set {
				ViewState ["NextPageText"] = value;
				RaisePropertyChanged ();
			}
		}
	
		[WebCategoryAttribute ("Behavior")]
		[NotifyParentPropertyAttribute (true)]
		[DefaultValueAttribute (10)]
		public int PageButtonCount {
			get {
				var ob = ViewState ["PageButtonCount"];
				if (ob != null) return (int) ob;
				return 10;
			}
			set {
				ViewState ["PageButtonCount"] = value;
				RaisePropertyChanged ();
			}
		}
	
		[WebCategoryAttribute ("Layout")]
		[DefaultValueAttribute (PagerPosition.Bottom)]
		[NotifyParentPropertyAttribute (true)]
		public PagerPosition Position {
			get {
				var ob = ViewState ["Position"];
				if (ob != null) return (PagerPosition) ob;
				return PagerPosition.Bottom;
			}
			set {
				ViewState ["Position"] = value;
			}
		}
	
		[WebCategoryAttribute ("Appearance")]
		[NotifyParentPropertyAttribute (true)]
		[UrlPropertyAttribute]
		[DefaultValueAttribute ("")]
		[EditorAttribute ("System.Web.UI.Design.ImageUrlEditor, " + Consts.AssemblySystem_Design, "System.Drawing.Design.UITypeEditor, " + Consts.AssemblySystem_Drawing)]
		public string PreviousPageImageUrl {
			get {
				var ob = ViewState ["PreviousPageImageUrl"];
				if (ob != null) return (string) ob;
				return string.Empty;
			}
			set {
				ViewState ["PreviousPageImageUrl"] = value;
				RaisePropertyChanged ();
			}
		}
	
		[WebCategoryAttribute ("Appearance")]
		[DefaultValueAttribute ("&lt;")]
		[NotifyParentPropertyAttribute (true)]
		public string PreviousPageText {
			get {
				var ob = ViewState ["PreviousPageText"];
				if (ob != null) return (string) ob;
				return "&lt;";
			}
			set {
				ViewState ["PreviousPageText"] = value;
				RaisePropertyChanged ();
			}
		}
	
		[WebCategoryAttribute ("Appearance")]
		[DefaultValueAttribute (true)]
		[NotifyParentPropertyAttribute (true)]
		public bool Visible {
			get {
				var ob = ViewState ["Visible"];
				if (ob != null) return (bool) ob;
				return true;
			}
			set {
				ViewState ["Visible"] = value;
			}
		}

		void RaisePropertyChanged ()
		{
			EventHandler eh = events [propertyChangedEvent] as EventHandler;
			if (eh != null)
				eh (this, EventArgs.Empty);
		}

		public override string ToString ()
		{
			return string.Empty;
		}

		void IStateManager.LoadViewState (object? savedState)
		{
			ViewState.LoadViewState (savedState);
		}
		
		object IStateManager.SaveViewState ()
		{
			return ViewState.SaveViewState();
		}
		
		void IStateManager.TrackViewState ()
		{
			ViewState.TrackViewState();
		}
		
		bool IStateManager.IsTrackingViewState
		{
			get { return ViewState.IsTrackingViewState; }
		}
		
		internal Table CreatePagerControl (int currentPage, int pageCount)
		{
			var table = new Table ();
			var row = new TableRow ();
			table.Rows.Add (row);

			var buttonCount = (Mode == PagerButtons.Numeric || Mode == PagerButtons.NumericFirstLast) ? PageButtonCount : 1;
			var first = buttonCount * (currentPage / buttonCount);
			var last = first + buttonCount;
			if (last > pageCount) {
				last = pageCount;
				if (last - first < buttonCount)
					first = last - buttonCount;
				if (first < 0)
					first = 0;
			}

			// First button
			if (Mode == PagerButtons.NumericFirstLast || Mode == PagerButtons.NextPreviousFirstLast) {
				if (first > 0)
					row.Cells.Add (CreateCell (FirstPageText, FirstPageImageUrl, "Page", "First"));
			}

			// Prev button
			if (Mode == PagerButtons.NextPrevious || Mode == PagerButtons.NextPreviousFirstLast) {
				if (first > 0)
					row.Cells.Add (CreateCell (PreviousPageText, PreviousPageImageUrl, "Page", "Prev"));
			}

			// Numbers
			if (Mode == PagerButtons.Numeric || Mode == PagerButtons.NumericFirstLast) {
				if (first > 0)
					row.Cells.Add (CreateCell ("...", string.Empty, "Page", first.ToString ()));
				for (var n = first; n < last; n++)
					row.Cells.Add (CreateCell ((n + 1).ToString (), string.Empty, (n != currentPage) ? "Page" : "", (n != currentPage) ? (n + 1).ToString () : ""));
				if (last < pageCount)
					row.Cells.Add (CreateCell ("...", string.Empty, "Page", (last + 1).ToString ()));
			}

			// Next button
			if (Mode == PagerButtons.NextPrevious || Mode == PagerButtons.NextPreviousFirstLast) {
				if (last < pageCount)
					row.Cells.Add (CreateCell (NextPageText, NextPageImageUrl, "Page", "Next"));
			}

			// Last button
			if (Mode == PagerButtons.NumericFirstLast || Mode == PagerButtons.NextPreviousFirstLast) {
				if (last < pageCount)
					row.Cells.Add (CreateCell (LastPageText, LastPageImageUrl, "Page", "Last"));
			}
			
			return table;
		}

		TableCell CreateCell (string text, string image, string command, string argument) {
			var cell = new TableCell ();

			Control button;
			if (String.IsNullOrEmpty (command)) {
				var l = new Label ();
				l.Text = text;
				button = l;
			}
			else
				button = (Control)DataControlButton.CreateButton (String.IsNullOrEmpty (image) ? ButtonType.Link : ButtonType.Image, ctrl, text, image, command, argument, true);

			cell.Controls.Add (button);
			return cell;
		}
	}
}

