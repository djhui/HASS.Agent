﻿using System.IO;
using Syncfusion.Windows.Forms;
using System.Text;
using HASS.Agent.Commands;
using HASS.Agent.Forms.Commands.CommandConfig;
using HASS.Agent.Functions;
using HASS.Agent.Models.Internal;
using HASS.Agent.Resources.Localization;
using HASS.Agent.Sensors;
using HASS.Agent.Shared.Enums;
using HASS.Agent.Shared.Extensions;
using HASS.Agent.Shared.Functions;
using HASS.Agent.Shared.Models.Config;
using HASS.Agent.Shared.Models.Internal;
using Newtonsoft.Json;
using HASS.Agent.Managers;
using static HASS.Agent.Shared.Functions.Inputs;

namespace HASS.Agent.Forms.Commands
{
	public partial class CommandsMod : MetroForm
	{
		internal readonly ConfiguredCommand Command;

		private readonly bool _serviceMode;
		private readonly string _serviceDeviceName;

		private bool _interfaceLockedWrongType;
		private bool _loading = true;

		private readonly Dictionary<int, string> _commandEntityTypes = new();
		private readonly Dictionary<string, string> _radioDevices = new();

		public CommandsMod(ConfiguredCommand command, bool serviceMode = false, string serviceDeviceName = "")
		{
			Command = command;

			_serviceMode = serviceMode;
			_serviceDeviceName = serviceDeviceName;

			InitializeComponent();

			BindListViewTheme();

			BindComboBoxTheme();
		}

		public CommandsMod(bool serviceMode = false, string serviceDeviceName = "")
		{
			Command = new ConfiguredCommand();

			_serviceMode = serviceMode;
			_serviceDeviceName = serviceDeviceName;

			InitializeComponent();

			BindListViewTheme();

			BindComboBoxTheme();
		}

		private void BindListViewTheme()
		{
			LvCommands.DrawItem += ListViewTheme.DrawItem;
			LvCommands.DrawSubItem += ListViewTheme.DrawSubItem;
			LvCommands.DrawColumnHeader += ListViewTheme.DrawColumnHeader;
		}

		private void BindComboBoxTheme()
		{
			CbEntityType.DrawItem += ComboBoxTheme.DrawDictionaryIntStringItem;
			CbConfigDropdown.DrawItem += ComboBoxTheme.DrawDictionaryStringStringItem;
		}

		private void CommandsMod_Load(object sender, EventArgs e)
		{
			// catch all key presses
			KeyPreview = true;

			foreach (CommandEntityType entityType in Enum.GetValues(typeof(CommandEntityType)))
			{
				var (key, description) = entityType.GetLocalizedDescriptionAndKey();
				_commandEntityTypes.Add(key, description);
			}

			_radioDevices.Add("none", Languages.SensorsMod_None);
			foreach (var radioName in RadioManager.AvailableRadioNames)
				_radioDevices[radioName] = radioName; //TODO: that's ugly

			CbEntityType.DataSource = new BindingSource(_commandEntityTypes, null);

			LvCommands.BeginUpdate();
			foreach (var command in CommandsManager.CommandInfoCards.Select(x => x.Value))
			{
				var lvCommand = new ListViewItem(command.Key.ToString());
				lvCommand.SubItems.Add(command.Name);
				lvCommand.SubItems.Add(command.AgentCompatible ? "√" : string.Empty);
				lvCommand.SubItems.Add(command.SatelliteCompatible ? "√" : string.Empty);
				lvCommand.SubItems.Add(command.ActionCompatible ? "√" : string.Empty);
				LvCommands.Items.Add(lvCommand);
			}
			LvCommands.EndUpdate();

			if (Command.Id == Guid.Empty)
			{
				Command.Id = Guid.NewGuid();
				Text = Languages.CommandsMod_Title_NewCommand;
				CbEntityType.Text = CommandEntityType.Switch.ToString();

				_loading = false;

				return;
			}

			// we're modding, load it
			LoadCommand();
			Text = Languages.CommandsMod_Title_ModCommand;

			// done
			_loading = false;
		}

		/// <summary>
		/// Loads the to-be-modded command
		/// </summary>
		private void LoadCommand()
		{
			var commandCard = CommandsManager.CommandInfoCards[Command.Type];

			foreach (ListViewItem lvi in LvCommands.Items)
			{
				if (lvi.Text != commandCard.Key.ToString())
					continue;

				lvi.Selected = true;
				LvCommands.SelectedItems[0].EnsureVisible();
				break;
			}

			var guiOk = SetType(false);
			if (!guiOk)
				return;

			TbName.Text = Command.EntityName;
			if (!string.IsNullOrWhiteSpace(TbName.Text))
				TbName.SelectionStart = TbName.Text.Length;

			TbFriendlyName.Text = Command.Name;

			var entityId = (int)Command.EntityType;
			CbEntityType.SelectedItem = new KeyValuePair<int, string>(entityId, _commandEntityTypes[entityId]);

			LblMqttTopic.Visible = commandCard.ActionCompatible;

			switch (commandCard.CommandType)
			{
				case CommandType.CustomCommand:
					TbSetting.Text = Command.Command;
					break;

				case CommandType.PowershellCommand:
					TbSetting.Text = Command.Command;
					break;

				case CommandType.KeyCommand:
					TbKeyCode.Text = Command.KeyCode.ToString();
					break;

				case CommandType.MultipleKeysCommand:
					var commands = new StringBuilder();
					foreach (var command in Command.Keys)
						commands.Append($"[{command}] ");

					TbSetting.Text = commands.ToString().Trim();
					break;

				case CommandType.LaunchUrlCommand:
					var urlInfo = Command.Command;
					if (string.IsNullOrEmpty(urlInfo))
						break;

					var urlPackage = JsonConvert.DeserializeObject<UrlInfo>(urlInfo);
					if (urlPackage == null)
						break;

					TbSetting.Text = urlPackage.Url;
					CbCommandSpecific.Checked = urlPackage.Incognito;
					break;

				case CommandType.CustomExecutorCommand:
					TbSetting.Text = Command.Command;
					break;

				case CommandType.SendWindowToFrontCommand:
					TbSetting.Text = Command.Command;
					break;

				case CommandType.WebViewCommand:
					TbSetting.Text = Command.Command;
					break;

				case CommandType.RadioCommand:
					CbConfigDropdown.SelectedItem = new KeyValuePair<string, string>(Command.Command, Command.Command);
					break;

                case CommandType.SetVolumeCommand:
                case CommandType.SetApplicationVolumeCommand:
                case CommandType.SetAudioOutputCommand:
                case CommandType.SetAudioInputCommand:
                    TbSetting.Text = Command.Command;
                    break;
            }

			CbRunAsLowIntegrity.CheckState = Command.RunAsLowIntegrity ? CheckState.Checked : CheckState.Unchecked;
		}

		/// <summary>
		/// Prepare the command for processing
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void BtnStore_Click(object sender, EventArgs e)
		{
			if (LvCommands.SelectedItems.Count == 0)
			{
				MessageBoxAdv.Show(this, Languages.CommandsMod_BtnStore_MessageBox1, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);

				return;
			}

			var commandId = int.Parse(LvCommands.SelectedItems[0].Text);
			var commandCard = CommandsManager.CommandInfoCards
				.Where(card => card.Value.Key == commandId)
				.Select(card => card.Value)
				.FirstOrDefault();

			if (commandCard == null)
			{
				MessageBoxAdv.Show(this, Languages.CommandsMod_BtnStore_MessageBox2, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);

				return;
			}

			if (CbEntityType.SelectedItem == null)
			{
				MessageBoxAdv.Show(this, Languages.CommandsMod_MessageBox_EntityType, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);

				return;
			}

			var item = (KeyValuePair<int, string>)CbEntityType.SelectedItem;
			var entityType = (CommandEntityType)item.Key;

            var name = TbName.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBoxAdv.Show(this, Languages.CommandsMod_MessageBox_Name, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                ActiveControl = TbName;
                
				return;
            }
            
			if (CompatHelper.HassVersionEqualOrOver("2023.8") && name.Contains(SharedHelperFunctions.GetSafeConfiguredDeviceName()))
            {
                MessageBoxAdv.Show(this, Languages.CommandsMod_BtnStore_DeviceNameInSensorName, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

			var friendlyName = string.IsNullOrEmpty(TbFriendlyName.Text.Trim()) ? name : TbFriendlyName.Text.Trim();

			var sanitized = SharedHelperFunctions.GetSafeValue(name);
			if (sanitized != name)
			{
				var confirmSanitize = MessageBoxAdv.Show(this, string.Format(Languages.CommandsMod_MessageBox_Sanitize, sanitized), Variables.MessageBoxTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
				if (confirmSanitize != DialogResult.OK)
				{
					ActiveControl = TbName;

					return;
				}

				TbName.Text = sanitized;
				name = sanitized;
			}

			if (!_serviceMode && Variables.Commands.Any(x => string.Equals(x.EntityName, name, StringComparison.InvariantCultureIgnoreCase) && x.Id != Command.Id.ToString()))
			{
				var confirm = MessageBoxAdv.Show(this, Languages.CommandsMod_BtnStore_MessageBox3, Variables.MessageBoxTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
				if (confirm != DialogResult.Yes)
				{
					ActiveControl = TbName;

					return;
				}
			}

			switch (commandCard.CommandType)
			{
				case CommandType.CustomCommand:
					var command = TbSetting.Text.Trim();
					if (string.IsNullOrEmpty(command))
					{
						var q = MessageBoxAdv.Show(this, Languages.CommandsMod_BtnStore_MessageBox4, Variables.MessageBoxTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
						if (q != DialogResult.Yes)
						{
							ActiveControl = TbSetting;

							return;
						}
					}
					Command.Command = command;
					break;

				case CommandType.PowershellCommand:
					var script = TbSetting.Text.Trim();
					if (string.IsNullOrEmpty(script))
					{
						var q = MessageBoxAdv.Show(this, Languages.CommandsMod_MessageBox_Action, Variables.MessageBoxTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
						if (q != DialogResult.Yes)
						{
							ActiveControl = TbSetting;

							return;
						}
					}
					Command.Command = script;
					break;

                case CommandType.KeyCommand:
                    var keycodeStr = TbKeyCode.Text.Trim();
                    if (string.IsNullOrEmpty(keycodeStr))
                    {
                        MessageBoxAdv.Show(this, Languages.CommandsMod_BtnStore_MessageBox5, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        ActiveControl = TbKeyCode;
                        return;
                    }

                    var parsed = Enum.TryParse(keycodeStr, out VirtualKeyShort enumKeycode);
                    if (!parsed)
                    {
                        MessageBoxAdv.Show(this, Languages.CommandsMod_BtnStore_MessageBox9, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        ActiveControl = TbKeyCode;
                        return;
                    }
                    Command.KeyCode = enumKeycode;
                    break;

				case CommandType.MultipleKeysCommand:
					var keysParsed = HelperFunctions.ParseMultipleKeys(TbSetting.Text.Trim(), out var keys, out var errorMsg);
					if (!keysParsed)
					{
						MessageBoxAdv.Show(this, string.Format(Languages.CommandsMod_BtnStore_MessageBox6, errorMsg), Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
						ActiveControl = TbSetting;

						return;
					}

                    if (keys.Count == 0)
                    {
                        var q = MessageBoxAdv.Show(this, Languages.CommandsMod_BtnStore_MessageBox4, Variables.MessageBoxTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (q != DialogResult.Yes)
                        {
                            ActiveControl = TbSetting;

                            return;
                        }
                    }

                    Command.Keys = keys;
					break;

				case CommandType.LaunchUrlCommand:
					var url = TbSetting.Text.Trim();
					if (string.IsNullOrEmpty(url))
					{
						var q = MessageBoxAdv.Show(this, Languages.CommandsMod_BtnStore_MessageBox7, Variables.MessageBoxTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
						if (q != DialogResult.Yes)
						{
							ActiveControl = TbSetting;

							return;
						}

						Command.Command = string.Empty;
					}
					else
					{
						var urlInfo = new UrlInfo
						{
							Url = url,
							Incognito = CbCommandSpecific.Checked
						};

						Command.Command = JsonConvert.SerializeObject(urlInfo);
					}
					break;

				case CommandType.CustomExecutorCommand:
					var executorCommand = TbSetting.Text.Trim();
					if (string.IsNullOrEmpty(executorCommand))
					{
						var q = MessageBoxAdv.Show(this, Languages.CommandsMod_MessageBox_Action, Variables.MessageBoxTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
						if (q != DialogResult.Yes)
						{
							ActiveControl = TbSetting;

							return;
						}
					}
					Command.Command = executorCommand;
					break;

				case CommandType.SendWindowToFrontCommand:
					var procName = TbSetting.Text.Trim();
					if (string.IsNullOrEmpty(procName))
					{
						var q = MessageBoxAdv.Show(this, Languages.CommandsMod_MessageBox_Action, Variables.MessageBoxTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
						if (q != DialogResult.Yes)
						{
							ActiveControl = TbSetting;

							return;
						}
					}
					Command.Command = procName;
					break;

				case CommandType.SetVolumeCommand:
					var volume = TbSetting.Text.Trim();
					if (string.IsNullOrEmpty(volume))
					{
						var q = MessageBoxAdv.Show(this, Languages.CommandsMod_MessageBox_Action2, Variables.MessageBoxTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
						if (q != DialogResult.Yes)
						{
							ActiveControl = TbSetting;

							return;
						}
					}
					else
					{
						var volParsed = int.TryParse(volume, out var vol);
						if (!volParsed || (vol is < 0 or > 100))
						{
							MessageBoxAdv.Show(this, Languages.CommandsMod_BtnStore_MessageBox10, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
							ActiveControl = TbSetting;

							return;
						}
						volume = vol.ToString();
					}
					Command.Command = volume;
					break;

                case CommandType.SetApplicationVolumeCommand:
                    var jsonString = TbSetting.Text.Trim();
                    try
                    {
                        if(string.IsNullOrWhiteSpace(jsonString))
                            throw new ArgumentException("json string cannot be blank");

                        var jsonObject = JsonConvert.DeserializeObject(jsonString);
                        Command.Command = jsonString;
                    }
                    catch
                    {
                        MessageBoxAdv.Show(this, Languages.CommandsMod_BtnStore_InvalidJson, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        ActiveControl = TbSetting;
                        return;
                    }
                    break;

                case CommandType.SetAudioOutputCommand:
                case CommandType.SetAudioInputCommand:
                    var audioDeviceName = TbSetting.Text.Trim();
                    if (string.IsNullOrEmpty(audioDeviceName))
                    {
                        var q = MessageBoxAdv.Show(this, Languages.CommandsMod_BtnStore_MessageBox8, Variables.MessageBoxTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (q != DialogResult.Yes)
                        {
                            ActiveControl = BtnConfigureCommand;
                            return;
                        }
                    }
                    Command.Command = audioDeviceName;
                    break;


                case CommandType.WebViewCommand:
                    var webview = TbSetting.Text.Trim();
                    if (string.IsNullOrEmpty(webview))
                    {
                        var q = MessageBoxAdv.Show(this, Languages.CommandsMod_BtnStore_MessageBox8, Variables.MessageBoxTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (q != DialogResult.Yes)
                        {
                            ActiveControl = BtnConfigureCommand;
                            return;
                        }

						Command.Command = string.Empty;
					}
					else
					{
						Command.Command = webview;
					}
					break;

				case CommandType.RadioCommand:
					if (CbConfigDropdown.SelectedItem != null)
					{
						var selectedItem = (KeyValuePair<string, string>)CbConfigDropdown.SelectedItem;
						if (selectedItem.Value == Languages.SensorsMod_None)
						{
							MessageBoxAdv.Show(this, Languages.CommandsMod_BtnStore_MessageBox11, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
							ActiveControl = CbConfigDropdown;
							return;
						}

						Command.Command = selectedItem.Key;
					}
					break;

                case CommandType.SwitchDesktopCommand:
                    if (!VirtualDesktopManager.Initialized)
                    {
                        MessageBoxAdv.Show(this, Languages.CommandsMod_BtnStore_VirtualDesktop_Unavailable, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        ActiveControl = TbSetting;
                        return;
                    }
                    break;
			}

			Command.RunAsLowIntegrity = CbRunAsLowIntegrity.CheckState == CheckState.Checked;

			Command.Type = commandCard.CommandType;
			Command.EntityType = entityType;
			Command.EntityName = name;
			Command.Name = friendlyName;

			DialogResult = DialogResult.OK;
		}

		private void LvCommands_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (_loading)
				return;

			SetType();

			ActiveControl = TbName;
			if (!string.IsNullOrWhiteSpace(TbName.Text))
				TbName.SelectionStart = TbName.Text.Length;
		}

		/// <summary>
		/// Change the UI depending on the selected type
		/// </summary>
		/// <param name="setDefaultValues"></param>
		private bool SetType(bool setDefaultValues = true)
		{
			if (LvCommands.SelectedItems.Count == 0)
			{
				if (_interfaceLockedWrongType)
					UnlockWrongClient();

				return false;
			}

			var commandId = int.Parse(LvCommands.SelectedItems[0].Text);
			var commandCard = CommandsManager.CommandInfoCards
				.Where(card => card.Value.Key == commandId)
				.Select(card => card.Value)
				.FirstOrDefault();

			if (commandCard == null)
				return false;

			if (_serviceMode && !commandCard.SatelliteCompatible)
			{
				LockWrongClient();

				return false;
			}

			if (!_serviceMode && !commandCard.AgentCompatible)
			{
				LockWrongClient();

				return false;
			}

			if (_interfaceLockedWrongType)
				UnlockWrongClient();

            if (setDefaultValues)
            {
                TbName.Text = commandCard.CommandType.GetCommandName();
                CbEntityType.Text = CommandEntityType.Switch.ToString();
                LblMqttTopic.Visible = commandCard.ActionCompatible;
            }

			TbSelectedType.Text = commandCard.CommandType.ToString();
			TbDescription.Text = CommandsManager.GetCommandDefaultInfo(commandCard.CommandType).Description;

			switch (commandCard.CommandType)
			{
				case CommandType.CustomCommand:
					SetCommandGui();
					break;

				case CommandType.PowershellCommand:
					SetPowershellGui();
					break;

				case CommandType.KeyCommand:
					SetKeyGui();
					break;

				case CommandType.MultipleKeysCommand:
					SetMultipleKeysGui();
					break;

				case CommandType.LaunchUrlCommand:
					SetUrlGui();
					break;

				case CommandType.CustomExecutorCommand:
					SetCustomExecutorUi();
					break;

				case CommandType.SendWindowToFrontCommand:
					SetSendWindowToFrontUi();
					break;

				case CommandType.WebViewCommand:
					SetWebViewUi();
					break;

				case CommandType.SetVolumeCommand:
					SetVolumeUi();
					break;

				case CommandType.SetApplicationVolumeCommand:
					SetApplicationVolumeUi();
					break;

                case CommandType.SetAudioOutputCommand:
                case CommandType.SetAudioInputCommand:
                    SetAudioOutputUi();
                    break;

				case CommandType.RadioCommand:
					CbConfigDropdown.DataSource = new BindingSource(_radioDevices, null);
					SetRadioUi();
					break;

				default:
					SetEmptyGui();
					break;
			}

			return true;
		}

		/// <summary>
		/// Change the UI to a 'command' type
		/// </summary>
		private void SetCommandGui()
		{
			Invoke(new MethodInvoker(delegate
			{
				SetEmptyGui();

				LblSetting.Text = Languages.CommandsMod_LblSetting_Command;
				LblSetting.Visible = true;

				TbSetting.Text = string.Empty;
				TbSetting.Visible = true;

				CbRunAsLowIntegrity.Visible = true;
				LblIntegrityInfo.Visible = true;
			}));
		}

		/// <summary>
		/// Change the UI to a 'powershell' type
		/// </summary>
		private void SetPowershellGui()
		{
			Invoke(new MethodInvoker(delegate
			{
				SetEmptyGui();

				LblSetting.Text = Languages.CommandsMod_LblSetting_CommandScript;
				LblSetting.Visible = true;

				TbSetting.Text = string.Empty;
				TbSetting.Visible = true;
			}));
		}

		/// <summary>
		/// Change the UI to a 'key' type
		/// </summary>
		private void SetKeyGui()
		{
			Invoke(new MethodInvoker(delegate
			{
				SetEmptyGui();

				LblSetting.Text = Languages.CommandsMod_LblSetting_KeyCode;
				LblSetting.Visible = true;

				TbKeyCode.Text = string.Empty;
				TbKeyCode.Visible = true;
			}));
		}

		/// <summary>
		/// Change the UI to a 'multiple keys' type
		/// </summary>
		private void SetMultipleKeysGui()
		{
			Invoke(new MethodInvoker(delegate
			{
				SetEmptyGui();

				LblSetting.Text = Languages.CommandsMod_LblSetting_KeyCodes;
				LblSetting.Visible = true;

				TbSetting.Text = string.Empty;
				TbSetting.Visible = true;
			}));
		}

		/// <summary>
		/// Change the UI to a 'url' type
		/// </summary>
		private void SetUrlGui()
		{
			Invoke(new MethodInvoker(delegate
			{
				SetEmptyGui();

				LblSetting.Text = Languages.CommandsMod_LblSetting_Url;
				LblSetting.Visible = true;

				TbSetting.Text = string.Empty;
				TbSetting.Visible = true;

				CbCommandSpecific.CheckState = CheckState.Unchecked;
				CbCommandSpecific.Text = Languages.CommandsMod_CbCommandSpecific_Incognito;

				if (string.IsNullOrEmpty(Variables.AppSettings.BrowserBinary))
				{
					LblInfo.Text = Languages.CommandsMod_LblInfo_Browser;
					LblInfo.Visible = true;

					CbCommandSpecific.CheckState = CheckState.Unchecked;
					CbCommandSpecific.Visible = false;
				}
				else
				{
					var browser = string.IsNullOrEmpty(Variables.AppSettings.BrowserName)
						? Path.GetFileNameWithoutExtension(Variables.AppSettings.BrowserBinary)
						: Variables.AppSettings.BrowserName;

					LblInfo.Text = string.Format(Languages.CommandsMod_LblInfo_BrowserSpecific, browser);
					LblInfo.Visible = true;

					CbCommandSpecific.Visible = true;
				}
			}));
		}

		/// <summary>
		/// Change the UI to a 'custom executor' type
		/// </summary>
		private void SetCustomExecutorUi()
		{
			Invoke(new MethodInvoker(delegate
			{
				SetEmptyGui();

				LblSetting.Text = Languages.CommandsMod_LblSetting_CommandScript;
				LblSetting.Visible = true;

				TbSetting.Text = string.Empty;
				TbSetting.Visible = true;

				if (string.IsNullOrEmpty(Variables.AppSettings.CustomExecutorBinary))
				{
					LblInfo.Text = Languages.CommandsMod_LblInfo_Executor;
				}
				else
				{
					var executor = string.IsNullOrEmpty(Variables.AppSettings.CustomExecutorName)
						? Path.GetFileNameWithoutExtension(Variables.AppSettings.CustomExecutorBinary)
						: Variables.AppSettings.CustomExecutorName;

					LblInfo.Text = string.Format(Languages.CommandsMod_LblInfo_ExecutorSpecific, executor);
				}

				LblInfo.Visible = true;
			}));
		}

		/// <summary>
		/// Change the UI to a 'sendwindowtofront' type
		/// </summary>
		private void SetSendWindowToFrontUi()
		{
			Invoke(new MethodInvoker(delegate
			{
				SetEmptyGui();

				LblSetting.Text = "process";
				LblSetting.Visible = true;

				TbSetting.Text = string.Empty;
				TbSetting.Visible = true;
			}));
		}

		/// <summary>
		/// Change the UI to a 'setvolume' type
		/// </summary>
		private void SetVolumeUi()
		{
			Invoke(new MethodInvoker(delegate
			{
				SetEmptyGui();

				LblSetting.Text = Languages.CommandsMod_LblSetting_VolumeRange;
                LblSetting.Visible = true;

                TbSetting.Text = string.Empty;
				TbSetting.Visible = true;
			}));
		}

        /// <summary>
        /// Change the UI to a 'setappvolume' type
        /// </summary>
        private void SetApplicationVolumeUi()
        {
            Invoke(new MethodInvoker(delegate
            {
                SetEmptyGui();

                LblSetting.Text = Languages.CommandsMod_LblSetting_JsonPayload;
                LblSetting.Visible = true;

                TbSetting.Text = string.Empty;
                TbSetting.Visible = true;
            }));
        }

        /// <summary>
        /// Change the UI to a 'setaudiooutput' type
        /// </summary>
        private void SetAudioOutputUi()
        {
            Invoke(new MethodInvoker(delegate
            {
                SetEmptyGui();

                LblSetting.Text = Languages.CommandsMod_LblSetting_AudioDeviceName;
                LblSetting.Visible = true;

                TbSetting.Text = string.Empty;
                TbSetting.Visible = true;
            }));
        }

        /// <summary>
        /// Change the UI to a 'webview' type
        /// </summary>
        private void SetWebViewUi()
        {
            Invoke(new MethodInvoker(delegate
            {
                SetEmptyGui();

				BtnConfigureCommand.Visible = true;
			}));
		}

		/// <summary>
		/// Change the UI to a 'radio' type
		/// </summary>
		private void SetRadioUi()
		{
			Invoke(new MethodInvoker(delegate
			{
				SetEmptyGui();

				LblSetting.Text = Languages.CommandsMod_LblSetting_Radio;
				LblSetting.Visible = true;

				CbConfigDropdown.Visible = true;
			}));
		}

		/// <summary>
		/// Change the UI to a general type
		/// </summary>
		private void SetEmptyGui()
		{
			Invoke(new MethodInvoker(delegate
			{
				LblSetting.Visible = false;

				TbSetting.Text = string.Empty;
				TbSetting.Visible = false;

				TbKeyCode.Text = string.Empty;
				TbKeyCode.Visible = false;

				CbRunAsLowIntegrity.CheckState = CheckState.Unchecked;
				CbRunAsLowIntegrity.Visible = false;
				LblIntegrityInfo.Visible = false;

				CbConfigDropdown.Visible = false;

				CbCommandSpecific.CheckState = CheckState.Unchecked;
				CbCommandSpecific.Visible = false;

				LblInfo.Text = string.Empty;
				LblInfo.Visible = false;

				BtnConfigureCommand.Visible = false;
			}));
		}

		private void TbDescription_LinkClicked(object sender, LinkClickedEventArgs e)
		{
			if (string.IsNullOrWhiteSpace(e.LinkText))
				return;

			if (!e.LinkText.ToLower().StartsWith("http"))
				return;

			HelperFunctions.LaunchUrl(e.LinkText);
		}

		private void CommandsMod_ResizeEnd(object sender, EventArgs e)
		{
			if (Variables.ShuttingDown || !IsHandleCreated || IsDisposed)
				return;

			try
			{
				// hide the pesky horizontal scrollbar
				ListViewTheme.ShowScrollBar(LvCommands.Handle, ListViewTheme.SB_HORZ, false);

				Refresh();
			}
			catch
			{
				// best effort
			}
		}

		private void LblIntegrityInfo_Click(object sender, EventArgs e)
		{
			var infoMsg = new StringBuilder();
			infoMsg.AppendLine(Languages.CommandsMod_LblIntegrityInfo_InfoMsg1);
			infoMsg.AppendLine(string.Empty);
			infoMsg.AppendLine(Languages.CommandsMod_LblIntegrityInfo_InfoMsg2);
			infoMsg.AppendLine(Languages.CommandsMod_LblIntegrityInfo_InfoMsg3);
			infoMsg.AppendLine(Languages.CommandsMod_LblIntegrityInfo_InfoMsg4);
			infoMsg.AppendLine(string.Empty);
			infoMsg.AppendLine(Languages.CommandsMod_LblIntegrityInfo_InfoMsg5);

			MessageBoxAdv.Show(this, infoMsg.ToString(), Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		private void CommandsMod_KeyUp(object sender, KeyEventArgs e)
		{
			if (e.KeyCode != Keys.Escape)
				return;

			Close();
		}

		private void CommandsMod_Layout(object sender, LayoutEventArgs e)
		{
			// hide the pesky horizontal scrollbar
			ListViewTheme.ShowScrollBar(LvCommands.Handle, ListViewTheme.SB_HORZ, false);
		}

		/// <summary>
		/// Locks the interface if the selected entity can't be added to the current client
		/// </summary>
		private void LockWrongClient()
		{
			if (InvokeRequired)
			{
				Invoke(new MethodInvoker(LockWrongClient));

				return;
			}

			_interfaceLockedWrongType = true;

			var requiredClient = _serviceMode ? "hass.agent" : "service";
			LblSpecificClient.Text = string.Format(Languages.CommandsMod_SpecificClient, requiredClient);

			LblSpecificClient.Visible = true;

			TbName.Enabled = false;
			TbName.Text = string.Empty;

			TbFriendlyName.Enabled = false;
			TbFriendlyName.Text = string.Empty;

			SetEmptyGui();

			BtnStore.Enabled = false;
		}

		/// <summary>
		/// Unlocks the interface if the selected entity can be added to the current client
		/// </summary>
		private void UnlockWrongClient()
		{
			if (InvokeRequired)
			{
				Invoke(new MethodInvoker(UnlockWrongClient));

				return;
			}

			_interfaceLockedWrongType = false;

			LblSpecificClient.Visible = false;

			TbName.Enabled = true;
			TbFriendlyName.Enabled = true;
			BtnStore.Enabled = true;
		}

		private void CbEntityType_SelectedIndexChanged(object sender, EventArgs e)
		{
			ActiveControl = TbName;
			if (!string.IsNullOrWhiteSpace(TbName.Text))
				TbName.SelectionStart = TbName.Text.Length;
		}

		private void LblMqttTopic_Click(object sender, EventArgs e)
		{
			if (CbEntityType.SelectedItem == null)
			{
				MessageBoxAdv.Show(this, Languages.CommandsMod_MessageBox_EntityType, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				ActiveControl = CbEntityType;

				return;
			}

			var item = (KeyValuePair<int, string>)CbEntityType.SelectedItem;
			var entityType = (CommandEntityType)item.Key;

			var deviceConfig = Variables.MqttManager?.GetDeviceConfigModel();
			if (deviceConfig == null)
			{
				MessageBoxAdv.Show(this, Languages.CommandsMod_LblMqttTopic_MessageBox1, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);

				return;
			}

			var name = TbName.Text.Trim();
			if (string.IsNullOrEmpty(name))
			{
				MessageBoxAdv.Show(this, Languages.CommandsMod_MessageBox_Name, Variables.MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				ActiveControl = TbName;

				return;
			}

			var topic = $"{Variables.MqttManager.MqttDiscoveryPrefix()}/{entityType.GetEnumMemberValue()}/{deviceConfig.Name}/{name}/action";

			var form = new CommandMqttTopic(topic);
			form.FormClosed += delegate { form.Dispose(); };
			form.Show(this);
		}

		private void LblActionInfo_Click(object sender, EventArgs e) => HelperFunctions.LaunchUrl("https://www.hass-agent.io/latest/getting-started/commands/");

		private void PbActionInfo_Click(object sender, EventArgs e) => HelperFunctions.LaunchUrl("https://www.hass-agent.io/latest/getting-started/commands/");

		private void BtnConfigureCommand_Click(object sender, EventArgs e)
		{
			var commandId = int.Parse(LvCommands.SelectedItems[0].Text);
			var commandCard = CommandsManager.CommandInfoCards.Where(card => card.Value.Key == commandId).Select(card => card.Value).FirstOrDefault();
			if (commandCard == null)
				return;

			switch (commandCard.CommandType)
			{
				case CommandType.WebViewCommand:
					using (var webviewConfig = new WebViewCommandConfig(TbSetting.Text))
					{
						webviewConfig.Opacity = 0;

						var ret = webviewConfig.ShowDialog();
						if (ret != DialogResult.OK)
							return;

						TbSetting.Text = JsonConvert.SerializeObject(webviewConfig.WebViewInfo);
					}
					break;
			}
		}

        private void TbKeyCode_KeyDown(object sender, KeyEventArgs e)
        {
            TbKeyCode.Text = Enum.GetName(typeof(VirtualKeyShort), e.KeyValue);
        }
    }
}
