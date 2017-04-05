using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.Design;

namespace ConfigGenerator {
	internal sealed class FlagCheckedListBox : CheckedListBox {
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		internal Enum EnumValue {
			get {
				object e = Enum.ToObject(EnumType, GetCurrentValue());
				return (Enum) e;
			}

			set {
				Items.Clear();
				_EnumValue = value; // Store the current enum value
				EnumType = value.GetType(); // Store enum type
				FillEnumMembers(); // Add items for enum members
				ApplyEnumValue(); // Check/uncheck items depending on enum value
			}
		}

		private Enum _EnumValue;
		private Type EnumType;
		private bool IsUpdatingCheckStates;

		internal FlagCheckedListBox() {
			// This call is required by the Windows.Forms Form Designer.
			InitializeComponent();
		}

		protected override void OnItemCheck(ItemCheckEventArgs e) {
			base.OnItemCheck(e);

			if (IsUpdatingCheckStates) {
				return;
			}

			// Get the checked/unchecked item
			FlagCheckedListBoxItem item = Items[e.Index] as FlagCheckedListBoxItem;
			// Update other items
			UpdateCheckedItems(item, e.NewValue);
		}

		// Adds an integer value and its associated description
		private void Add(int v, string c) {
			FlagCheckedListBoxItem item = new FlagCheckedListBoxItem(v, c);
			Items.Add(item);
		}

		// Checks/unchecks items based on the current value of the enum variable
		private void ApplyEnumValue() {
			int intVal = (int) Convert.ChangeType(_EnumValue, typeof(int));
			UpdateCheckedItems(intVal);
		}

		// Adds items to the checklistbox based on the members of the enum
		private void FillEnumMembers() {
			foreach (string name in Enum.GetNames(EnumType)) {
				object val = Enum.Parse(EnumType, name);
				int intVal = (int) Convert.ChangeType(val, typeof(int));

				Add(intVal, name);
			}
		}

		// Gets the current bit value corresponding to all checked items
		private int GetCurrentValue() => (from object t in Items select t as FlagCheckedListBoxItem).Where((item, i) => (item != null) && GetItemChecked(i)).Aggregate(0, (current, item) => current | item.Value);

		#region Component Designer generated code

		private void InitializeComponent() {
			// 
			// FlaggedCheckedListBox
			// 
			CheckOnClick = true;
		}

		#endregion

		// Checks/Unchecks items depending on the give bitvalue
		private void UpdateCheckedItems(int value) {
			IsUpdatingCheckStates = true;

			// Iterate over all items
			for (int i = 0; i < Items.Count; i++) {
				FlagCheckedListBoxItem item = Items[i] as FlagCheckedListBoxItem;
				if (item == null) {
					continue;
				}

				if (item.Value == 0) {
					SetItemChecked(i, value == 0);
				} else {
					// If the bit for the current item is on in the bitvalue, check it
					if (((item.Value & value) == item.Value) && (item.Value != 0)) {
						SetItemChecked(i, true);
					}
					// Otherwise uncheck it
					else {
						SetItemChecked(i, false);
					}
				}
			}

			IsUpdatingCheckStates = false;
		}

		// Updates items in the checklistbox
		// composite = The item that was checked/unchecked
		// cs = The check state of that item
		private void UpdateCheckedItems(FlagCheckedListBoxItem composite, CheckState cs) {
			// If the value of the item is 0, call directly.
			if (composite.Value == 0) {
				UpdateCheckedItems(0);
			}

			// Get the total value of all checked items
			int sum = (from object t in Items select t as FlagCheckedListBoxItem).Where((item, i) => (item != null) && GetItemChecked(i)).Aggregate(0, (current, item) => current | item.Value);

			// If the item has been unchecked, remove its bits from the sum
			if (cs == CheckState.Unchecked) {
				sum = sum & ~composite.Value;
			}
			// If the item has been checked, combine its bits with the sum
			else {
				sum |= composite.Value;
			}

			// Update all items in the checklistbox based on the final bit value
			UpdateCheckedItems(sum);
		}
	}

	// Represents an item in the checklistbox
	internal sealed class FlagCheckedListBoxItem {
		internal readonly int Value;

		private readonly string Caption;

		internal FlagCheckedListBoxItem(int v, string c) {
			Value = v;
			Caption = c;
		}

		public override string ToString() => Caption;
	}

	// UITypeEditor for flag enums
	internal sealed class FlagEnumUiEditor : UITypeEditor {
		// The checklistbox
		private readonly FlagCheckedListBox FlagEnumCb;

		internal FlagEnumUiEditor() => FlagEnumCb = new FlagCheckedListBox { BorderStyle = BorderStyle.None };

		public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value) {
			if ((context?.PropertyDescriptor == null) || (provider == null)) {
				return null;
			}

			IWindowsFormsEditorService edSvc = (IWindowsFormsEditorService) provider.GetService(typeof(IWindowsFormsEditorService));

			if (edSvc == null) {
				return null;
			}

			Enum e = (Enum) Convert.ChangeType(value, context.PropertyDescriptor.PropertyType);
			FlagEnumCb.EnumValue = e;
			edSvc.DropDownControl(FlagEnumCb);
			return FlagEnumCb.EnumValue;
		}

		public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) => UITypeEditorEditStyle.DropDown;
	}
}