using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using SayMore.Model.Files;
using SayMore.UI.LowLevelControls;

namespace SayMore.UI.ComponentEditors
{
	/// ----------------------------------------------------------------------------------------
	/// <summary>
	/// This is kind of an experiment at the moment...
	/// </summary>
	/// ----------------------------------------------------------------------------------------
	[ProvideProperty("IsBound", typeof(IComponent))]
	[ProvideProperty("IsComponentFileId", typeof(IComponent))]
	public class BindingHelper : Component, IExtenderProvider
	{
		private readonly Func<Control, string> MakeIdFromControlName = (ctrl => ctrl.Name.TrimStart('_'));
		private readonly Func<string, string> MakeControlNameFromId = (id => "_" + id);

		public delegate bool GetBoundControlValueHandler(BindingHelper helper,
			Control boundControl, out string newValue);

		public event GetBoundControlValueHandler GetBoundControlValue;

		private Container components;
		private readonly Dictionary<Control, bool> _extendedControls = new Dictionary<Control, bool>();
		private List<Control> _boundControls;
		private ComponentFile _file;
		private Control _componentFileIdControl;

		#region Constructors
		/// ------------------------------------------------------------------------------------
		public BindingHelper()
		{
			// Required for Windows.Forms Class Composition Designer support
			components = new Container();
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Constructor for instance that supports Class Composition designer.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		public BindingHelper(IContainer container) : this()
		{
			container.Add(this);
		}

		#endregion

		#region IExtenderProvider Members
		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Extend only certain controls. Add new ones as they are needed.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		public bool CanExtend(object extendee)
		{
			var ctrl = extendee as Control;
			if (ctrl == null)
				return false;

			var extend = (new[]
			{
				typeof(TextBox),
				typeof(DateTimePicker),
				typeof(ComboBox),
				typeof(MultiValueComboBox)
			}).Contains(ctrl.GetType());

			if (extend && !_extendedControls.ContainsKey(ctrl))
				_extendedControls[ctrl] = true;

			return extend;
		}

		#endregion

		#region Properties provided by this extender
		/// ------------------------------------------------------------------------------------
		[Localizable(false)]
		[Category("BindingHelper Properties")]
		public bool GetIsBound(object obj)
		{
			bool isBound;
			return (_extendedControls.TryGetValue(obj as Control, out isBound) ? isBound : false);
		}

		/// ------------------------------------------------------------------------------------
		public void SetIsBound(object obj, bool bind)
		{
			var ctrl = obj as Control;
			_extendedControls[ctrl] = bind;

			// Do this just in case this is being called from outside the initialize
			// components method and after the component file has been set.
			if (!bind)
				UnBindControl(ctrl);
		}

		/// ------------------------------------------------------------------------------------
		[Localizable(false)]
		[Category("BindingHelper Properties")]
		public bool GetIsComponentFileId(object obj)
		{
			return (_componentFileIdControl == obj);
		}

		/// ------------------------------------------------------------------------------------
		public void SetIsComponentFileId(object obj, bool isFileId)
		{
			if (obj is Control && isFileId)
				_componentFileIdControl = (Control)obj;
		}

		#endregion

		/// ------------------------------------------------------------------------------------
		public void SetComponentFile(ComponentFile file)
		{
			if (DesignMode)
				return;

			if (_file != null)
				_file.MetadataValueChanged -= HandleValueChangedOutsideBinder;

			_file = file;
			_file.MetadataValueChanged += HandleValueChangedOutsideBinder;

			// First, collect only the extended controls that are bound.
			_boundControls = _extendedControls.Where(x => x.Value).Select(x => x.Key).ToList();

			foreach (var ctrl in _boundControls)
			{
				ctrl.Font = SystemFonts.IconTitleFont;
				BindControl(ctrl);
			}
		}

		/// ------------------------------------------------------------------------------------
		private void BindControl(Control ctrl)
		{
			if (!_boundControls.Contains(ctrl))
				_boundControls.Add(ctrl);

			if (ctrl is ComboBox && ((ComboBox)ctrl).DropDownStyle == ComboBoxStyle.DropDownList)
				((ComboBox)ctrl).SelectedValueChanged -= HandleBoundComboValueChanged;
			else
				ctrl.Validating -= HandleValidatingControl;

			ctrl.Disposed -= HandleDisposed;
			UpdateControlValueFromField(ctrl);
			ctrl.Disposed += HandleDisposed;

			if (ctrl is ComboBox && ((ComboBox)ctrl).DropDownStyle == ComboBoxStyle.DropDownList)
				((ComboBox)ctrl).SelectedValueChanged += HandleBoundComboValueChanged;
			else
				ctrl.Validating += HandleValidatingControl;
		}

		/// ------------------------------------------------------------------------------------
		private void UnBindControl(Control ctrl)
		{
			ctrl.Disposed -= HandleDisposed;

			if (ctrl is ComboBox && ((ComboBox)ctrl).DropDownStyle == ComboBoxStyle.DropDownList)
				((ComboBox)ctrl).SelectedValueChanged -= HandleBoundComboValueChanged;
			else
				ctrl.Validating -= HandleValidatingControl;

			if (_boundControls != null && _boundControls.Contains(ctrl))
				_boundControls.Remove(ctrl);
		}

		/// ------------------------------------------------------------------------------------
		private void HandleDisposed(object sender, EventArgs e)
		{
			UnBindControl(sender as Control);
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// Called when something happens (like chosing a preset) which modifies the values
		/// of the file directly, and we need to update the UI
		/// </summary>
		/// ------------------------------------------------------------------------------------
		public void UpdateFieldsFromFile()
		{
			foreach (var ctrl in _boundControls)
				UpdateControlValueFromField(ctrl);
		}

		/// ------------------------------------------------------------------------------------
		private void UpdateControlValueFromField(Control ctrl)
		{
			var key = MakeIdFromControlName(ctrl);
			var stringValue = _file.GetStringValue(key, string.Empty);
			try
			{
				ctrl.Text = stringValue;
			}
			catch (Exception error)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(
					new Palaso.Reporting.ShowOncePerSessionBasedOnExactMessagePolicy(), error,
					"SayMore had a problem displaying the {0}, which had a value of {1}. You should report this problem to the developers by clicking 'Details' below.",
					key, stringValue);
			}
		}

		/// ------------------------------------------------------------------------------------
		private Control GetBoundControlFromKey(string key)
		{
			var ctrlName = MakeControlNameFromId(key);
			return _boundControls.FirstOrDefault(c => c.Name == ctrlName);
		}

		/// ------------------------------------------------------------------------------------
		public string GetValue(string key)
		{
			return _file.GetStringValue(key, string.Empty);
		}

		/// ------------------------------------------------------------------------------------
		public string SetValue(string key, string value)
		{
			string failureMessage;
			_file.MetadataValueChanged -= HandleValueChangedOutsideBinder;
			var modifiedValue = _file.SetValue(key, value, out failureMessage);
			_file.MetadataValueChanged += HandleValueChangedOutsideBinder;

			if (failureMessage != null)
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(failureMessage);

			//enchance: don't save so often, leave it to some higher level
			_file.Save();

			return modifiedValue;
		}

		/// ------------------------------------------------------------------------------------
		private void HandleBoundComboValueChanged(object sender, EventArgs e)
		{
			HandleValidatingControl(sender, new CancelEventArgs());
		}

		/// ------------------------------------------------------------------------------------
		private void HandleValidatingControl(object sender, CancelEventArgs e)
		{
			var ctrl = (Control)sender;
			var key = MakeIdFromControlName(ctrl);

			string newValue = null;

			var gotNewValueFromDelegate = (GetBoundControlValue != null &&
				GetBoundControlValue(this, ctrl, out newValue));

			if (!gotNewValueFromDelegate)
				newValue = ctrl.Text.Trim();

			// Don't bother doing anything if the old value is the same as the new value.
			var oldValue = _file.GetStringValue(key, null);
			if (oldValue != null && oldValue == newValue)
				return;

			string failureMessage;

			_file.MetadataValueChanged -= HandleValueChangedOutsideBinder;

			newValue = (_componentFileIdControl == ctrl ?
				_file.TryChangeChangeId(newValue, out failureMessage) :
				_file.SetValue(key, newValue, out failureMessage));

			_file.MetadataValueChanged += HandleValueChangedOutsideBinder;

			if (!gotNewValueFromDelegate)
				ctrl.Text = newValue;

			if (failureMessage != null)
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(failureMessage);

			//enchance: don't save so often, leave it to some higher level
			if (_componentFileIdControl != ctrl)
				_file.Save();
		}

		/// ------------------------------------------------------------------------------------
		/// <summary>
		/// We get this event when a meta data value changes from somewhere other than from in
		/// this binding helper. When we get this message for bound fields, we need to make
		/// sure the bound control associated with the field, gets updated.
		/// </summary>
		/// ------------------------------------------------------------------------------------
		private void HandleValueChangedOutsideBinder(ComponentFile file, string fieldId,
			string oldValue, string newValue)
		{
			var ctrl = GetBoundControlFromKey(fieldId);
			if (ctrl != null)
				UpdateControlValueFromField(ctrl);
		}
	}
}