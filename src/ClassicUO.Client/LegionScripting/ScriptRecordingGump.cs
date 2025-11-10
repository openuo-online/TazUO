using System;
using System.Collections.Generic;
using System.Linq;
using ClassicUO.Assets;
using ClassicUO.Game;
using ClassicUO.Game.Data;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Input;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.LegionScripting
{
    internal class ScriptRecordingGump : NineSliceGump
    {
        private ModernScrollArea _scrollArea;
        private NiceButton _recordButton;
        private NiceButton _pauseButton;
        private NiceButton _clearButton;
        private NiceButton _copyButton;
        private NiceButton _saveButton;
        private Label _titleBar;
        private Label _statusText;
        private Label _durationText;
        private Label _actionCountText;
        private VBoxContainer _actionList;
        private Checkbox _recordPausesCheckbox;

        private static int _lastX = 100, _lastY = 100;
        private static int _lastWidth = 400, _lastHeight = 500;
        private const int MIN_WIDTH = 350;
        private const int MIN_HEIGHT = 400;

        private List<RecordedAction> _displayedActions = new List<RecordedAction>();

        public ScriptRecordingGump() : base(World.Instance, _lastX, _lastY, _lastWidth, _lastHeight, ModernUIConstants.ModernUIPanel, ModernUIConstants.ModernUIPanel_BoderSize, false)
        {
            CanCloseWithRightClick = true;
            AcceptMouseInput = true;
            CanMove = true;

            BuildGump();
            SubscribeToRecorderEvents();
            UpdateUI();
        }

        private void BuildGump()
        {
            // Title bar
            _titleBar = new Label(Resources.ResGumps.ScriptRecordingStopped, true, 52, font: 1)
            {
                X = BorderSize + 10,
                Y = BorderSize + 10
            };
            Add(_titleBar);

            int currentY = _titleBar.Y + _titleBar.Height + 15;

            // Control buttons
            _recordButton = new NiceButton(BorderSize + 10, currentY, 80, 25, ButtonAction.Activate, Resources.ResGumps.ScriptRecord, 0, TEXT_ALIGN_TYPE.TS_CENTER)
            {
                ButtonParameter = (int)RecordingAction.ToggleRecord,
                DisplayBorder = true
            };
            _recordButton.MouseUp += OnButtonClick;
            Add(_recordButton);

            _pauseButton = new NiceButton(BorderSize + 100, currentY, 60, 25, ButtonAction.Activate, Resources.ResGumps.ScriptPause, 0, TEXT_ALIGN_TYPE.TS_CENTER)
            {
                ButtonParameter = (int)RecordingAction.Pause,
                IsEnabled = false,
                DisplayBorder = true
            };
            _pauseButton.MouseUp += OnButtonClick;
            Add(_pauseButton);

            _clearButton = new NiceButton(BorderSize + 170, currentY, 60, 25, ButtonAction.Activate, Resources.ResGumps.ScriptClear, 0, TEXT_ALIGN_TYPE.TS_CENTER)
            {
                ButtonParameter = (int)RecordingAction.Clear,
                DisplayBorder = true
            };
            _clearButton.MouseUp += OnButtonClick;
            Add(_clearButton);

            currentY += 35;

            // Status information
            _statusText = new Label(Resources.ResGumps.ScriptStatusReady, true, 0xFFFF, font: 1)
            {
                X = BorderSize + 10,
                Y = currentY
            };
            Add(_statusText);

            currentY += _statusText.Height + 5;

            _durationText = new Label(string.Format(Resources.ResGumps.ScriptDuration, 0, 0), true, 999, font: 1)
            {
                X = BorderSize + 10,
                Y = currentY
            };
            Add(_durationText);

            currentY += _durationText.Height + 5;

            _actionCountText = new Label(string.Format(Resources.ResGumps.ScriptActions, 0), true, 999, font: 1)
            {
                X = BorderSize + 10,
                Y = currentY
            };
            Add(_actionCountText);

            currentY += _actionCountText.Height + 10;

            // Record pauses option
            _recordPausesCheckbox = new Checkbox(0x00D2, 0x00D3, Resources.ResGumps.ScriptIncludePauses, 1, 0xFFFF)
            {
                X = BorderSize + 10,
                Y = currentY,
                IsChecked = true
            };
            Add(_recordPausesCheckbox);

            currentY += _recordPausesCheckbox.Height + 15;

            // Action list
            var actionListLabel = new Label(Resources.ResGumps.ScriptRecordedActions, true, 0x35, font: 1)
            {
                X = BorderSize + 10,
                Y = currentY
            };
            Add(actionListLabel);

            currentY += actionListLabel.Height + 5;

            // Scrollable action list
            int listHeight = Height - currentY - 80; // Leave space for bottom buttons
            _scrollArea = new ModernScrollArea(BorderSize + 10, currentY, Width - 2 * BorderSize - 20, listHeight)
            {
                AcceptMouseInput = true,
                ScrollbarBehaviour = ScrollbarBehaviour.ShowWhenDataExceedFromView
            };
            Add(_scrollArea);

            _actionList = new VBoxContainer(Width - 2 * BorderSize - 35)
            {
                X = 0,
                Y = 0
            };
            _scrollArea.Add(_actionList);

            // Bottom buttons
            int bottomY = Height - BorderSize - 35;
            _copyButton = new NiceButton(BorderSize + 10, bottomY, 100, 25, ButtonAction.Activate, Resources.ResGumps.ScriptCopyScript, 0, TEXT_ALIGN_TYPE.TS_CENTER)
            {
                ButtonParameter = (int)RecordingAction.Copy,
                DisplayBorder = true
            };
            _copyButton.MouseUp += OnButtonClick;
            Add(_copyButton);

            _saveButton = new NiceButton(BorderSize + 120, bottomY, 100, 25, ButtonAction.Activate, Resources.ResGumps.ScriptSaveScript, 0, TEXT_ALIGN_TYPE.TS_CENTER)
            {
                ButtonParameter = (int)RecordingAction.Save,
                DisplayBorder = true
            };
            _saveButton.MouseUp += OnButtonClick;
            Add(_saveButton);
        }

        private void SubscribeToRecorderEvents()
        {
            ScriptRecorder.Instance.RecordingStateChanged += OnRecordingStateChanged;
            ScriptRecorder.Instance.ActionRecorded += OnActionRecorded;
        }

        private void UnsubscribeFromRecorderEvents()
        {
            ScriptRecorder.Instance.RecordingStateChanged -= OnRecordingStateChanged;
            ScriptRecorder.Instance.ActionRecorded -= OnActionRecorded;
        }

        private void OnRecordingStateChanged(object sender, EventArgs e) => UpdateUI();

        private void OnActionRecorded(object sender, RecordedAction action)
        {
            _displayedActions.Add(action);
            UpdateActionList();
            UpdateActionCount();
        }

        private void OnButtonClick(object sender, MouseEventArgs e)
        {
            if (sender is NiceButton button)
            {
                var action = (RecordingAction)button.ButtonParameter;

                switch (action)
                {
                    case RecordingAction.ToggleRecord:
                        if (ScriptRecorder.Instance.IsRecording)
                            ScriptRecorder.Instance.StopRecording();
                        else
                            ScriptRecorder.Instance.StartRecording();
                        break;

                    case RecordingAction.Pause:
                        if (ScriptRecorder.Instance.IsPaused)
                            ScriptRecorder.Instance.ResumeRecording();
                        else
                            ScriptRecorder.Instance.PauseRecording();
                        break;

                    case RecordingAction.Clear:
                        ScriptRecorder.Instance.ClearRecording();
                        _displayedActions.Clear();
                        UpdateActionList();
                        break;

                    case RecordingAction.Copy:
                        CopyScriptToClipboard();
                        break;

                    case RecordingAction.Save:
                        SaveScriptToFile();
                        break;
                }
            }
        }

        private void OnActionButtonClick(object sender, MouseEventArgs e)
        {
            if (sender is NiceButton button)
            {
                string actionType = button.Tag as string;
                int index = button.ButtonParameter;

                switch (actionType)
                {
                    case "delete":
                        DeleteAction(index);
                        break;

                    case "moveup":
                        MoveActionUp(index);
                        break;

                    case "movedown":
                        MoveActionDown(index);
                        break;
                }
            }
        }

        private void DeleteAction(int index)
        {
            if (index >= 0 && index < _displayedActions.Count)
            {
                _displayedActions.RemoveAt(index);
                ScriptRecorder.Instance.RemoveActionAt(index);
                UpdateActionList();
                UpdateActionCount();
            }
        }

        private void MoveActionUp(int index)
        {
            if (index > 0 && index < _displayedActions.Count)
            {
                // Swap with previous action
                RecordedAction temp = _displayedActions[index];
                _displayedActions[index] = _displayedActions[index - 1];
                _displayedActions[index - 1] = temp;

                ScriptRecorder.Instance.SwapActions(index, index - 1);
                UpdateActionList();
            }
        }

        private void MoveActionDown(int index)
        {
            if (index >= 0 && index < _displayedActions.Count - 1)
            {
                // Swap with next action
                RecordedAction temp = _displayedActions[index];
                _displayedActions[index] = _displayedActions[index + 1];
                _displayedActions[index + 1] = temp;

                ScriptRecorder.Instance.SwapActions(index, index + 1);
                UpdateActionList();
            }
        }

        private void UpdateUI()
        {
            ScriptRecorder recorder = ScriptRecorder.Instance;

            // Update title
            if (recorder.IsRecording)
            {
                _titleBar.Text = recorder.IsPaused ? Resources.ResGumps.ScriptRecordingPaused : Resources.ResGumps.ScriptRecordingRecording;
            }
            else
            {
                _titleBar.Text = Resources.ResGumps.ScriptRecordingStopped;
            }

            // Update buttons
            _recordButton.SetText(recorder.IsRecording ? Resources.ResGumps.ScriptStop : Resources.ResGumps.ScriptRecord);
            _pauseButton.IsEnabled = recorder.IsRecording;
            _pauseButton.SetText(recorder.IsPaused ? Resources.ResGumps.ScriptResume : Resources.ResGumps.ScriptPause);

            // Update status
            if (recorder.IsRecording)
            {
                _statusText.Text = recorder.IsPaused ? Resources.ResGumps.ScriptStatusPaused : Resources.ResGumps.ScriptStatusRecording;
            }
            else
            {
                _statusText.Text = Resources.ResGumps.ScriptStatusReady;
            }

            // Update duration and action count
            UpdateDuration();
            UpdateActionCount();
        }

        private void UpdateDuration()
        {
            uint duration = ScriptRecorder.Instance.RecordingDuration;
            uint seconds = duration / 1000;
            uint minutes = seconds / 60;
            seconds %= 60;

            _durationText.Text = string.Format(Resources.ResGumps.ScriptDuration, minutes, seconds);
        }

        private void UpdateActionCount() => _actionCountText.Text = string.Format(Resources.ResGumps.ScriptActions, ScriptRecorder.Instance.ActionCount);

        private void UpdateActionList()
        {
            _actionList.Clear();

            // Show all actions, not just recent ones, to allow proper manipulation
            for (int i = 0; i < _displayedActions.Count; i++)
            {
                Control actionContainer = CreateActionRowContainer(_displayedActions[i], i);
                _actionList.Add(actionContainer);
            }
        }

        private Control CreateActionRowContainer(RecordedAction action, int index)
        {
            var container = new HitBox(0, 0, _actionList.Width - 10, 25, alpha: 0.0f);

            // Action text label
            string actionText = FormatActionForDisplay(action);
            var actionLabel = new Label(actionText, true, 0xFFFF, font: 1, maxwidth: container.Width - 90)
            {
                X = 0,
                Y = 2
            };
            container.Add(actionLabel);

            // Delete button
            var deleteButton = new NiceButton(container.Width - 85, 2, 25, 20, ButtonAction.Activate, "×", 0, TEXT_ALIGN_TYPE.TS_CENTER)
            {
                ButtonParameter = index,
                DisplayBorder = true,
                Tag = "delete"
            };
            deleteButton.MouseUp += OnActionButtonClick;
            container.Add(deleteButton);

            // Move up button
            var moveUpButton = new NiceButton(container.Width - 57, 2, 25, 20, ButtonAction.Activate, "↑", 0, TEXT_ALIGN_TYPE.TS_CENTER)
            {
                ButtonParameter = index,
                DisplayBorder = true,
                IsEnabled = index > 0,
                Tag = "moveup"
            };
            moveUpButton.MouseUp += OnActionButtonClick;
            container.Add(moveUpButton);

            // Move down button
            var moveDownButton = new NiceButton(container.Width - 29, 2, 25, 20, ButtonAction.Activate, "↓", 0, TEXT_ALIGN_TYPE.TS_CENTER)
            {
                ButtonParameter = index,
                DisplayBorder = true,
                IsEnabled = index < _displayedActions.Count - 1,
                Tag = "movedown"
            };
            moveDownButton.MouseUp += OnActionButtonClick;
            container.Add(moveDownButton);

            return container;
        }

        private string FormatActionForDisplay(RecordedAction action)
        {
            switch (action.ActionType.ToLower())
            {
                case "walk":
                    string walkDir = action.Parameters.ContainsKey("direction") ? Utility.GetDirectionString(Utility.GetDirection(action.Parameters["direction"].ToString())) : "?";
                    return string.Format(Resources.ResGumps.ScriptActionWalk, walkDir);
                case "run":
                    string runDir = action.Parameters.ContainsKey("direction") ? Utility.GetDirectionString(Utility.GetDirection(action.Parameters["direction"].ToString())) : "?";
                    return string.Format(Resources.ResGumps.ScriptActionRun, runDir);
                case "cast":
                    object spell = action.Parameters.ContainsKey("spell") ? action.Parameters["spell"] : "?";
                    return string.Format(Resources.ResGumps.ScriptActionCast, spell);
                case "say":
                    string message = action.Parameters.ContainsKey("message") ? action.Parameters["message"].ToString() : "?";
                    if (message.Length > 30)
                        message = message.Substring(0, 27) + "...";
                    return string.Format(Resources.ResGumps.ScriptActionSay, message);
                case "useitem":
                    object serial = action.Parameters.ContainsKey("serial") ? action.Parameters["serial"] : "?";
                    return string.Format(Resources.ResGumps.ScriptActionUseItem, serial);
                case "dragdrop":
                    object from = action.Parameters.ContainsKey("from") ? action.Parameters["from"] : "?";
                    object to = action.Parameters.ContainsKey("to") ? action.Parameters["to"] : "?";
                    return string.Format(Resources.ResGumps.ScriptActionDragDrop, from, to);
                case "target":
                    object targetSerial = action.Parameters.ContainsKey("serial") ? action.Parameters["serial"] : "?";
                    return string.Format(Resources.ResGumps.ScriptActionTarget, targetSerial);
                case "targetlocation":
                    object targX = action.Parameters.ContainsKey("x") ? action.Parameters["x"] : "?";
                    object targY = action.Parameters.ContainsKey("y") ? action.Parameters["y"] : "?";
                    object targZ = action.Parameters.ContainsKey("z") ? action.Parameters["z"] : "?";
                    return string.Format(Resources.ResGumps.ScriptActionTargetLocation, targX, targY, targZ);
                case "opencontainer":
                    object openSerial = action.Parameters.ContainsKey("serial") ? action.Parameters["serial"] : "?";
                    object openType = action.Parameters.ContainsKey("type") ? action.Parameters["type"] : Resources.ResGumps.ScriptContainer;
                    return string.Format(Resources.ResGumps.ScriptActionOpenContainer, openType, openSerial);
                case "closecontainer":
                    object closeSerial = action.Parameters.ContainsKey("serial") ? action.Parameters["serial"] : "?";
                    object closeType = action.Parameters.ContainsKey("type") ? action.Parameters["type"] : Resources.ResGumps.ScriptContainer;
                    return string.Format(Resources.ResGumps.ScriptActionCloseContainer, closeType, closeSerial);
                case "attack":
                    object attackSerial = action.Parameters.ContainsKey("serial") ? action.Parameters["serial"] : "?";
                    return string.Format(Resources.ResGumps.ScriptActionAttack, attackSerial);
                case "bandageself":
                    return Resources.ResGumps.ScriptActionBandageSelf;
                case "contextmenu":
                    object contextSerial = action.Parameters.ContainsKey("serial") ? action.Parameters["serial"] : "?";
                    object contextIndex = action.Parameters.ContainsKey("index") ? action.Parameters["index"] : "?";
                    return string.Format(Resources.ResGumps.ScriptActionContextMenu, contextSerial, contextIndex);
                case "useskill":
                    object skillName = action.Parameters.ContainsKey("skill") ? action.Parameters["skill"] : "?";
                    return string.Format(Resources.ResGumps.ScriptActionUseSkill, skillName);
                case "equipitem":
                    object equipSerial = action.Parameters.ContainsKey("serial") ? action.Parameters["serial"] : "?";
                    object layer = action.Parameters.ContainsKey("layer") ? action.Parameters["layer"] : "?";
                    return string.Format(Resources.ResGumps.ScriptActionEquipItem, equipSerial, layer);
                case "replygump":
                    object gumpButton = action.Parameters.ContainsKey("button") ? action.Parameters["button"] : "?";
                    object gumpId = action.Parameters.ContainsKey("gumpid") ? action.Parameters["gumpid"] : "?";
                    return string.Format(Resources.ResGumps.ScriptActionReplyGump, gumpButton, gumpId);
                case "headmsg":
                    string headMsgText = action.Parameters.ContainsKey("message") ? action.Parameters["message"].ToString() : "?";
                    object headSerial = action.Parameters.ContainsKey("serial") ? action.Parameters["serial"] : "?";
                    if (headMsgText.Length > 20) headMsgText = headMsgText.Substring(0, 17) + "...";
                    return string.Format(Resources.ResGumps.ScriptActionHeadMsg, headMsgText, headSerial);
                case "partymsg":
                    string partyMsgText = action.Parameters.ContainsKey("message") ? action.Parameters["message"].ToString() : "?";
                    if (partyMsgText.Length > 25) partyMsgText = partyMsgText.Substring(0, 22) + "...";
                    return string.Format(Resources.ResGumps.ScriptActionPartyMsg, partyMsgText);
                case "guildmsg":
                    string guildMsgText = action.Parameters.ContainsKey("message") ? action.Parameters["message"].ToString() : "?";
                    if (guildMsgText.Length > 25) guildMsgText = guildMsgText.Substring(0, 22) + "...";
                    return string.Format(Resources.ResGumps.ScriptActionGuildMsg, guildMsgText);
                case "allymsg":
                    string allyMsgText = action.Parameters.ContainsKey("message") ? action.Parameters["message"].ToString() : "?";
                    if (allyMsgText.Length > 25) allyMsgText = allyMsgText.Substring(0, 22) + "...";
                    return string.Format(Resources.ResGumps.ScriptActionAllyMsg, allyMsgText);
                case "whispermsg":
                    string whisperMsgText = action.Parameters.ContainsKey("message") ? action.Parameters["message"].ToString() : "?";
                    if (whisperMsgText.Length > 25) whisperMsgText = whisperMsgText.Substring(0, 22) + "...";
                    return string.Format(Resources.ResGumps.ScriptActionWhisperMsg, whisperMsgText);
                case "yellmsg":
                    string yellMsgText = action.Parameters.ContainsKey("message") ? action.Parameters["message"].ToString() : "?";
                    if (yellMsgText.Length > 25) yellMsgText = yellMsgText.Substring(0, 22) + "...";
                    return string.Format(Resources.ResGumps.ScriptActionYellMsg, yellMsgText);
                case "emotemsg":
                    string emoteMsgText = action.Parameters.ContainsKey("message") ? action.Parameters["message"].ToString() : "?";
                    if (emoteMsgText.Length > 25) emoteMsgText = emoteMsgText.Substring(0, 22) + "...";
                    return string.Format(Resources.ResGumps.ScriptActionEmoteMsg, emoteMsgText);
                case "mount":
                    object mountSerial = action.Parameters.ContainsKey("serial") ? action.Parameters["serial"] : "?";
                    return string.Format(Resources.ResGumps.ScriptActionMount, mountSerial);
                case "dismount":
                    return Resources.ResGumps.ScriptActionDismount;
                case "toggleability":
                    object ability = action.Parameters.ContainsKey("ability") ? action.Parameters["ability"] : "?";
                    return string.Format(Resources.ResGumps.ScriptActionToggleAbility, ability);
                case "virtue":
                    object virtue = action.Parameters.ContainsKey("virtue") ? action.Parameters["virtue"] : "?";
                    return string.Format(Resources.ResGumps.ScriptActionVirtue, virtue);
                case "waitforgump":
                    return Resources.ResGumps.ScriptActionWaitForGump;
                default:
                    return string.Format(Resources.ResGumps.ScriptActionUnknown, action.ActionType);
            }
        }

        private void CopyScriptToClipboard()
        {
            try
            {
                string script = ScriptRecorder.Instance.GenerateScript(_recordPausesCheckbox.IsChecked);
                SDL3.SDL.SDL_SetClipboardText(script);
                GameActions.Print(Resources.ResGumps.ScriptCopiedToClipboard);
            }
            catch (Exception ex)
            {
                GameActions.Print(string.Format(Resources.ResGumps.ScriptFailedToCopy, ex.Message));
            }
        }

        private void SaveScriptToFile()
        {
            try
            {
                string script = ScriptRecorder.Instance.GenerateScript(_recordPausesCheckbox.IsChecked);
                string fileName = $"recorded_script_{DateTime.Now:yyyyMMdd_HHmmss}.py";
                string filePath = System.IO.Path.Combine(LegionScripting.ScriptPath, fileName);

                System.IO.File.WriteAllText(filePath, script);
                GameActions.Print(string.Format(Resources.ResGumps.ScriptSavedAs, fileName));
            }
            catch (Exception ex)
            {
                GameActions.Print(string.Format(Resources.ResGumps.ScriptFailedToSave, ex.Message));
            }
        }

        public override void Update()
        {
            base.Update();

            // Update duration display if recording
            if (ScriptRecorder.Instance.IsRecording && !ScriptRecorder.Instance.IsPaused)
            {
                UpdateDuration();
            }
        }

        protected override void OnResize(int oldWidth, int oldHeight, int newWidth, int newHeight)
        {
            base.OnResize(oldWidth, oldHeight, newWidth, newHeight);

            // Save position and size
            _lastX = X;
            _lastY = Y;
            _lastWidth = Width;
            _lastHeight = Height;

            // Rebuild gump with new dimensions
            BuildGump();
            UpdateUI();
        }

        public override void Dispose()
        {
            UnsubscribeFromRecorderEvents();
            base.Dispose();
        }

        private enum RecordingAction
        {
            ToggleRecord,
            Pause,
            Clear,
            Copy,
            Save
        }
    }
}
