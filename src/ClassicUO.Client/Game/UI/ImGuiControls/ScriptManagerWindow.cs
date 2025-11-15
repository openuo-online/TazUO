using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using ClassicUO.Game;
using ClassicUO.Game.Managers;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Game.UI.ImGuiControls.Legion;
using ClassicUO.Input;
using ClassicUO.LegionScripting;
using ClassicUO.Utility;
using ClassicUO.Utility.Logging;
using ImGuiNET;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace ClassicUO.Game.UI.ImGuiControls
{
    public class ScriptManagerWindow : SingletonImGuiWindow<ScriptManagerWindow>
    {
        private readonly HashSet<string> _collapsedGroups = new HashSet<string>();
        private bool _showContextMenu = false;
        private string _contextMenuGroup = "";
        private string _contextMenuSubGroup = "";
        private ScriptFile _contextMenuScript = null;
        private Vector2 _contextMenuPosition;
        private bool _pendingReload = false;
        private uint _pendingReloadTime = 0;
        private bool _shouldCancelRename = false;

        private const string SCRIPT_HEADER =
            "# See examples at" +
            "\n#   https://github.com/PlayTazUO/PublicLegionScripts/" +
            "\n# Or documentation at" +
            "\n#   https://tazuo.org/legion/api/";

        private const string EXAMPLE_LSCRIPT =
            SCRIPT_HEADER +
            @"
player = API.Player
delay = 8
diffhits = 10

while True:
    if player.HitsMax - player.Hits > diffhits or player.IsPoisoned:
        if API.BandageSelf():
            API.CreateCooldownBar(delay, 'Bandaging...', 21)
            API.Pause(delay)
        else:
            API.SysMsg('WARNING: No bandages!', 32)
            break
    API.Pause(0.5)";

        private const string NOGROUPTEXT = "No group";
        private const string RepeatStartMarker = "# <ClassicUORepeatStart>";
        private const string RepeatEndMarker = "# <ClassicUORepeatEnd>";

        // Helper classes for cleaner state management

        private class RenameState
        {
            public bool IsRenaming => Script != null || !string.IsNullOrEmpty(GroupName);
            public ScriptFile Script { get; set; }
            public string GroupName { get; set; }
            public string GroupParent { get; set; }
            public string Buffer { get; set; } = "";

            public void StartScriptRename(ScriptFile script, string initialName)
            {
                Clear();
                Script = script;
                Buffer = initialName;
            }

            public void StartGroupRename(string groupName, string parentGroup)
            {
                Clear();
                GroupName = groupName;
                GroupParent = parentGroup;
                Buffer = groupName;
            }

            public void Clear()
            {
                Script = null;
                GroupName = "";
                GroupParent = "";
                Buffer = "";
            }
        }

        private class ScriptRunSettings
        {
            public int RepeatCount { get; set; } = 500;
            public bool RepeatEnabled { get; set; }
            public string RepeatBuffer { get; set; } = "1";
        }

        private class DialogState
        {
            public bool ShowNewScript { get; set; }
            public bool ShowNewGroup { get; set; }
            public bool ShowRenameGroup { get; set; }
            public bool ShowDeleteConfirm { get; set; }

            public string NewScriptName { get; set; } = "";
            public string NewGroupName { get; set; } = "";

            public string DeleteTitle { get; set; } = "";
            public string DeleteMessage { get; set; } = "";
            public ScriptFile ScriptToDelete { get; set; }
            public string GroupToDelete { get; set; } = "";
            public string GroupToDeleteParent { get; set; } = "";

            public void ClearAll()
            {
                ShowNewScript = false;
                ShowNewGroup = false;
                ShowRenameGroup = false;
                ShowDeleteConfirm = false;
                NewScriptName = "";
                NewGroupName = "";
                DeleteTitle = "";
                DeleteMessage = "";
                ScriptToDelete = null;
                GroupToDelete = "";
                GroupToDeleteParent = "";
            }

            public void ShowScriptDeleteDialog(ScriptFile script)
            {
                ScriptToDelete = script;
                GroupToDelete = "";
                GroupToDeleteParent = "";
                DeleteTitle = "Delete Script";
                DeleteMessage = $"Are you sure you want to delete '{script.FileName}'?\n\nThis action cannot be undone.";
                ShowDeleteConfirm = true;
            }

            public void ShowGroupDeleteDialog(string groupName, string parentGroup)
            {
                ScriptToDelete = null;
                GroupToDelete = groupName;
                GroupToDeleteParent = parentGroup;
                DeleteTitle = "Delete Group";
                DeleteMessage = $"Are you sure you want to delete the group '{groupName}'?\n\nThis will permanently delete the folder and ALL scripts inside it.\nThis action cannot be undone.";
                ShowDeleteConfirm = true;
            }
        }

        private readonly RenameState _renameState = new RenameState();
        private readonly DialogState _dialogState = new DialogState();
        private readonly Dictionary<string, ScriptRunSettings> _scriptRunSettings = new Dictionary<string, ScriptRunSettings>();

        private ScriptManagerWindow() : base(ImGuiTranslations.Get("Script Manager"))
        {
            WindowFlags = ImGuiWindowFlags.None;
            _pendingReload = true;
        }

        public void RequestReload(uint delayMs = 0)
        {
            if (delayMs == 0)
            {
                _pendingReload = true;
                _pendingReloadTime = 0;
            }
            else
            {
                uint target = Time.Ticks + delayMs;
                if (target == 0) target = 1;
                _pendingReloadTime = target;
            }
        }

        public override void DrawContent()
        {
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 4));
            ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 0);
            ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 1.0f);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, ImGuiTheme.Current.Primary * 0.8f);
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, ImGuiTheme.Current.Primary);
            ImGui.PushStyleColor(ImGuiCol.Header, ImGuiTheme.Current.Primary);

            if (!_pendingReload && _pendingReloadTime != 0 && Time.Ticks >= _pendingReloadTime)
            {
                _pendingReload = true;
                _pendingReloadTime = 0;
            }

            // Load scripts if needed
            if (_pendingReload)
            {
                LegionScripting.LegionScripting.LoadScriptsFromFile();
                _pendingReload = false;
            }

            // Cancel rename if user clicks outside (but give buttons priority)
            if (_renameState.IsRenaming && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                // Check if we clicked outside the rename input area
                // We need to set a flag and check it after the input is drawn
                _shouldCancelRename = true;
            }

            // Top menu bar - fixed at the top, not affected by scrolling
            DrawMenuBar();
            ImGui.SeparatorText(ImGuiTranslations.Get("Scripts"));
            ImGui.Spacing();
            // Create a scrollable child region for the script groups
            Vector2 contentRegionAvail = ImGui.GetContentRegionAvail();

            if (ImGui.BeginChild("ScriptGroupsScrollable", new Vector2(contentRegionAvail.X, contentRegionAvail.Y), ImGuiChildFlags.None, ImGuiWindowFlags.None))
            {
                // Organize scripts by groups
                Dictionary<string, Dictionary<string, List<ScriptFile>>> groupsMap = OrganizeScripts();

                // Draw script groups within the scrollable area
                DrawScriptGroups(groupsMap);
            }
            ImGui.EndChild();

            // Handle context menus and dialogs
            DrawContextMenus();
            DrawDialogs();

            // Reset cancel rename flag if it wasn't used
            _shouldCancelRename = false;

            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar(5);
        }

        private void DrawMenuBar()
        {
            if (ImGui.Button(ImGuiTranslations.Get("Menu") + "##MenuBtn"))
            {
                ImGui.OpenPopup("ScriptManagerMenu");

            }
            ImGui.SameLine();

            if (ImGui.Button(ImGuiTranslations.Get("Script Recording") + "##RecordBtn"))
            {
                    UIManager.Add(new ScriptRecordingGump());
            }
            ImGui.SameLine();

            if (ImGui.Button(ImGuiTranslations.Get("Add +") + "##AddBtn"))
            {
                _showContextMenu = true;
                _contextMenuGroup = ""; // Root level
                _contextMenuSubGroup = NOGROUPTEXT; // This will show both "New Script" and "New Group" options
                _contextMenuScript = null;
                _contextMenuPosition = ImGui.GetMousePos();
            }

            if (ImGui.BeginPopup("ScriptManagerMenu"))
            {
                if (ImGui.MenuItem(ImGuiTranslations.Get("Refresh")))
                {
                    _pendingReload = true;
                }

                if (ImGui.MenuItem(ImGuiTranslations.Get("Public Script Browser")))
                {
                    ScriptBrowser.Show();
                }

                if (ImGui.MenuItem(ImGuiTranslations.Get("Script Recording")))
                {
                    UIManager.Add(new ScriptRecordingGump());
                }

                if (ImGui.MenuItem(ImGuiTranslations.Get("Scripting Info")))
                {
                    ScriptingInfoGump.Show();
                }

                if (ImGui.MenuItem(ImGuiTranslations.Get("Persistent Variables")))
                {
                    PersistentVarsWindow.Show();
                }

                if (ImGui.MenuItem(ImGuiTranslations.Get("Running Scripts")))
                {
                    RunningScriptsWindow.Show();
                }

                bool disableCache = LegionScripting.LegionScripting.LScriptSettings.DisableModuleCache;
                if (ImGui.Checkbox(ImGuiTranslations.Get("Disable Module Cache"), ref disableCache))
                {
                    LegionScripting.LegionScripting.LScriptSettings.DisableModuleCache = disableCache;
                }
                ImGui.EndPopup();
            }
        }

        private Dictionary<string, Dictionary<string, List<ScriptFile>>> OrganizeScripts()
        {
            var groupsMap = new Dictionary<string, Dictionary<string, List<ScriptFile>>>
            {
                { "", new Dictionary<string, List<ScriptFile>> { { "", new List<ScriptFile>() } } }
            };

            foreach (ScriptFile sf in LegionScripting.LegionScripting.LoadedScripts)
            {
                if (!groupsMap.ContainsKey(sf.Group))
                    groupsMap[sf.Group] = new Dictionary<string, List<ScriptFile>>();

                if (!groupsMap[sf.Group].ContainsKey(sf.SubGroup))
                    groupsMap[sf.Group][sf.SubGroup] = new List<ScriptFile>();

                groupsMap[sf.Group][sf.SubGroup].Add(sf);
            }

            return groupsMap;
        }

        private void DrawScriptGroups(Dictionary<string, Dictionary<string, List<ScriptFile>>> groupsMap)
        {
            foreach (KeyValuePair<string, Dictionary<string, List<ScriptFile>>> group in groupsMap)
            {
                string groupName = string.IsNullOrEmpty(group.Key) ? NOGROUPTEXT : group.Key;
                DrawGroup(groupName, group.Value, "");
            }
        }

        private void DrawGroup(string groupName, Dictionary<string, List<ScriptFile>> subGroups, string parentGroup)
        {
            string fullGroupPath = string.IsNullOrEmpty(parentGroup) ? groupName : Path.Combine(parentGroup, groupName);

            // Initialize collapsed state from settings if not already in our set
            string normalizedGroupName = groupName == NOGROUPTEXT ? "" : groupName;
            string normalizedParentGroup = parentGroup == NOGROUPTEXT ? "" : parentGroup;
            string parentSpacer = string.IsNullOrEmpty(parentGroup) ? string.Empty : "   ";

            bool isCollapsedInSettings = string.IsNullOrEmpty(normalizedParentGroup)
                ? LegionScripting.LegionScripting.IsGroupCollapsed(normalizedGroupName)
                : LegionScripting.LegionScripting.IsGroupCollapsed(normalizedParentGroup, normalizedGroupName);

            if (isCollapsedInSettings && !_collapsedGroups.Contains(fullGroupPath))
                _collapsedGroups.Add(fullGroupPath);

            bool isCollapsed = _collapsedGroups.Contains(fullGroupPath);
            // Group header with expand/collapse button and context menu
            ImGui.PushID(fullGroupPath);
            // Create custom expand/collapse button with custom symbols
            string expandSymbol = isCollapsed ? "+" : "-"; // Plus for collapsed, minus for expanded
            // Use a square button with larger size for better visibility
            ImGui.Text($"{parentSpacer}[ {expandSymbol} ] ");

            ImGui.SameLine(0, 2); // Small spacing between button and text

            bool nodeOpen = !isCollapsed;
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(5, 10));
            // Use Selectable instead of Text to get hover highlighting
            bool groupSelected = false;
            string displayGroupName = groupName == NOGROUPTEXT ? ImGuiTranslations.Get("No group") : groupName;
            if (ImGui.Selectable(displayGroupName, groupSelected, ImGuiSelectableFlags.SpanAllColumns))
            {
                // Single click on group name - toggle expand/collapse
                if (!ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    ToggleGroupState(isCollapsed, fullGroupPath, normalizedParentGroup, normalizedGroupName);
                }
            }

            // Right-click context menu for group
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _showContextMenu = true;
                _contextMenuGroup = parentGroup;
                _contextMenuSubGroup = groupName;
                _contextMenuScript = null;
                _contextMenuPosition = ImGui.GetMousePos();
            }

            // Accept drag and drop for moving scripts to this group
            if (ImGui.BeginDragDropTarget())
            {
                // Highlight the drop target area with primary theme color
                ImDrawListPtr drawList = ImGui.GetWindowDrawList();
                Vector2 itemMin = ImGui.GetItemRectMin();
                Vector2 itemMax = ImGui.GetItemRectMax();
                uint highlightColor = ImGui.ColorConvertFloat4ToU32(ImGuiTheme.Current.Primary * 0.5f); // Semi-transparent primary color
                drawList.AddRectFilled(itemMin, itemMax, highlightColor);

                unsafe
                {
                    ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload("SCRIPT_FILE");
                    if (payload.NativePtr != null)
                    {
                        // Extract the script file path from payload
                        byte[] payloadData = new byte[payload.DataSize];
                        System.Runtime.InteropServices.Marshal.Copy(payload.Data, payloadData, 0, (int)payload.DataSize);
                        string scriptPath = System.Text.Encoding.UTF8.GetString(payloadData);

                        // Find the script and move it to this group
                        ScriptFile script = LegionScripting.LegionScripting.LoadedScripts.FirstOrDefault(s => s.FullPath == scriptPath);
                        if (script != null)
                        {
                            // Determine the correct target group hierarchy based on current level
                            string targetGroup, targetSubGroup;

                            if (string.IsNullOrEmpty(parentGroup) || parentGroup == NOGROUPTEXT)
                            {
                                // Dropping into a top-level group
                                targetGroup = normalizedGroupName;
                                targetSubGroup = "";
                            }
                            else
                            {
                                // Dropping into a subgroup
                                targetGroup = normalizedParentGroup;
                                targetSubGroup = normalizedGroupName;
                            }

                            MoveScriptToGroup(script, targetGroup, targetSubGroup);
                        }
                    }
                }
                ImGui.EndDragDropTarget();
            }

            // If node is open, render children without extra indentation
            if (nodeOpen)
            {
                // Draw subgroups and scripts
                foreach (KeyValuePair<string, List<ScriptFile>> subGroup in subGroups)
                {
                    if (!string.IsNullOrEmpty(subGroup.Key))
                    {
                        // This is a subgroup
                        var subGroupData = new Dictionary<string, List<ScriptFile>> { { "", subGroup.Value } };
                        DrawGroup(subGroup.Key, subGroupData, groupName);
                    }
                    else
                    {
                        // These are scripts directly in this group
                        foreach (ScriptFile script in subGroup.Value)
                        {
                            DrawScript(script, parentSpacer);
                        }
                    }
                }
            }
            ImGui.PopStyleVar(1);
            ImGui.PopID();
        }

        private void DrawScript(ScriptFile script, string spacer)
        {
            ImGui.PushID(script.FullPath);

            ImGui.Text(spacer);
            ImGui.SameLine();

            ScriptRunSettings runSettings = GetRunSettings(script);

            ImGui.SetNextItemWidth(100);
            string repeatBuffer = runSettings.RepeatBuffer;
            bool repeatInputChanged = ImGui.InputText(
                $"##repeatCount{script.FullPath}",
                ref repeatBuffer,
                8,
                ImGuiInputTextFlags.CharsDecimal
            );
            if (repeatInputChanged)
            {
                runSettings.RepeatBuffer = repeatBuffer;
                if (int.TryParse(repeatBuffer, out int parsed) && parsed >= 1)
                {
                    runSettings.RepeatCount = parsed;
                    runSettings.RepeatBuffer = parsed.ToString();
                    if (runSettings.RepeatEnabled)
                    {
                        UpdateRepeatDelay(script, runSettings);
                    }
                }
            }
            if (!ImGui.IsItemActive())
            {
                if (!int.TryParse(runSettings.RepeatBuffer, out int parsed) || parsed < 1)
                {
                    runSettings.RepeatCount = 1;
                    runSettings.RepeatBuffer = "1";
                    if (runSettings.RepeatEnabled)
                    {
                        UpdateRepeatDelay(script, runSettings);
                    }
                }
            }
            ImGui.SameLine();

            bool repeatEnabled = runSettings.RepeatEnabled;
            if (ImGui.Checkbox($"重复##repeatToggle{script.FullPath}", ref repeatEnabled))
            {
                if (repeatEnabled)
                {
                    if (!ApplyRepeatWrapper(script, runSettings))
                    {
                        repeatEnabled = false;
                    }
                }
                else
                {
                    RemoveRepeatWrapper(script);
                }

                runSettings.RepeatEnabled = repeatEnabled;
            }
            ImGui.SameLine();

            // Get script display name (without extension)
            string displayName = script.FileName;
            int lastDotIndex = displayName.LastIndexOf('.');
            if (lastDotIndex != -1)
                displayName = displayName.Substring(0, lastDotIndex);

            // Check if script is playing
            bool isPlaying = script.IsPlaying || (script.GetScript != null && script.GetScript.IsPlaying);

            // Draw play/stop button
            string buttonText = isPlaying ? ImGuiTranslations.Get("Stop") : ImGuiTranslations.Get("Play");
            Vector4 buttonColor = isPlaying
                ? new Vector4(0.2f, 0.6f, 0.2f, 1.0f) // Green for play
                : ImGuiTheme.Current.Primary;


            ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonColor * 1.2f);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, buttonColor * 0.8f);

            if (ImGui.Button(buttonText, new Vector2(50, 0)))
            {
                if (isPlaying)
                    LegionScripting.LegionScripting.StopScript(script);
                else
                    LegionScripting.LegionScripting.PlayScript(script);
            }

            ImGui.PopStyleColor(3);

            // Autostart indicator
            ImGui.SameLine();
            bool hasGlobalAutostart = LegionScripting.LegionScripting.AutoLoadEnabled(script, true);
            bool hasCharacterAutostart = LegionScripting.LegionScripting.AutoLoadEnabled(script, false);

            if (hasGlobalAutostart || hasCharacterAutostart)
            {
                Vector4 autostartColor = hasGlobalAutostart
                    ? new Vector4(1.0f, 0.8f, 0.0f, 1.0f)  // Gold for global autostart
                    : new Vector4(0.0f, 0.8f, 1.0f, 1.0f); // Cyan for character autostart

                ImGui.PushStyleColor(ImGuiCol.Text, autostartColor);
                string indicator = hasGlobalAutostart ? "[G]" : "[C]";
                ImGui.Text(indicator);
                ImGui.PopStyleColor();

                if (ImGui.IsItemHovered())
                {
                    string tooltip = hasGlobalAutostart ? ImGuiTranslations.Get("Autostart: All characters") : ImGuiTranslations.Get("Autostart: This character");
                    ImGui.SetTooltip(tooltip);
                }
                ImGui.SameLine();
            }

            // Draw script name or rename input
            if (_renameState.Script == script)
            {
                // Show rename input - Enter to save, Escape or click outside to cancel
                ImGui.SetKeyboardFocusHere();
                ImGui.SetNextItemWidth(150);
                string buffer = _renameState.Buffer;
                if (ImGui.InputText($"##rename{script.FullPath}", ref buffer, 256, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    _renameState.Buffer = buffer;
                    PerformRename(script);
                }
                else
                {
                    _renameState.Buffer = buffer;
                }

                // Check for Escape key to cancel rename
                if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                {
                    _renameState.Clear();
                }

                // Check if we should cancel rename due to clicking outside
                if (_shouldCancelRename)
                {
                    // If the input text was clicked/hovered, don't cancel
                    if (!ImGui.IsItemHovered() && !ImGui.IsItemActive())
                    {
                        _renameState.Clear();
                    }
                    _shouldCancelRename = false; // Reset the flag
                }
            }
            else
            {
                // Normal script display with native double-click detection
                bool isSelected = false;
                Vector4 scriptNameColor = new Vector4(0.7f, 1.0f, 0.7f, 1.0f);
                ImGui.PushStyleColor(ImGuiCol.Text, scriptNameColor);
                ImGui.Selectable($"{displayName}", isSelected);
                ImGui.PopStyleColor();

                // Use native ImGUI double-click detection
                if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    // Start renaming
                    _renameState.StartScriptRename(script, displayName);
                }


                // Begin drag source for script
                if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.None))
                {
                    // Set payload to script file path
                    unsafe
                    {
                        byte[] scriptPathBytes = System.Text.Encoding.UTF8.GetBytes(script.FullPath);
                        fixed (byte* ptr = scriptPathBytes)
                        {
                            ImGui.SetDragDropPayload("SCRIPT_FILE", new IntPtr(ptr), (uint)scriptPathBytes.Length);
                        }
                    }

                    // Tooltip showing what's being dragged
                    ImGui.Text(ImGuiTranslations.Get("Moving: ") + displayName);
                    ImGui.EndDragDropSource();
                }
            }

            // Tooltip with full filename (only when not renaming)
            if (_renameState.Script != script && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(script.FileName);
            }

            // Right-click context menu for script (works on both button and name)
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                _showContextMenu = true;
                _contextMenuScript = script;
                _contextMenuGroup = "";
                _contextMenuSubGroup = "";
                _contextMenuPosition = ImGui.GetMousePos();
            }
            ImGui.PopID();
        }

        private ScriptRunSettings GetRunSettings(ScriptFile script)
        {
            if (!_scriptRunSettings.TryGetValue(script.FullPath, out ScriptRunSettings settings))
            {
                settings = new ScriptRunSettings();
                if (TryReadRepeatSettings(script, out int detectedDelay))
                {
                    settings.RepeatEnabled = true;
                    settings.RepeatCount = detectedDelay;
                }
                settings.RepeatBuffer = settings.RepeatCount.ToString();
                _scriptRunSettings[script.FullPath] = settings;
            }

            return settings;
        }

        private bool ApplyRepeatWrapper(ScriptFile script, ScriptRunSettings settings)
        {
            try
            {
                string normalized = ReadScriptNormalized(script);

                if (string.IsNullOrWhiteSpace(normalized))
                {
                    GameActions.Print("脚本内容为空，无法启用重复执行。", 33);
                    return false;
                }

                if (normalized.Contains(RepeatStartMarker))
                {
                    UpdateRepeatDelay(script, settings);
                    return true;
                }

                SplitScriptSections(normalized, out string header, out string body);
                string indentedBody = IndentBody(body);
                string delayValue = GetDelaySeconds(settings);

                var sb = new StringBuilder();
                sb.Append(header);
                sb.Append(RepeatStartMarker).Append('\n');
                sb.Append("while True:\n");
                if (!string.IsNullOrEmpty(indentedBody))
                {
                    sb.Append(indentedBody);
                    if (!indentedBody.EndsWith("\n", StringComparison.Ordinal))
                    {
                        sb.Append('\n');
                    }
                }
                sb.Append("    API.Pause(").Append(delayValue).Append(")\n");
                sb.Append(RepeatEndMarker).Append('\n');

                SaveScriptNormalized(script, sb.ToString());

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to enable repeat for {script.FileName}: {ex}");
                GameActions.Print($"启用脚本重复失败: {ex.Message}", 33);
                return false;
            }
        }

        private bool RemoveRepeatWrapper(ScriptFile script)
        {
            try
            {
                string normalized = ReadScriptNormalized(script);
                int startIndex = normalized.IndexOf(RepeatStartMarker, StringComparison.Ordinal);
                if (startIndex < 0)
                {
                    return false;
                }

                int whileIndex = normalized.IndexOf("while True:\n", startIndex, StringComparison.Ordinal);
                if (whileIndex < 0)
                {
                    return false;
                }

                int bodyStart = whileIndex + "while True:\n".Length;
                int pauseIndex = normalized.IndexOf("\n    API.Pause(", bodyStart, StringComparison.Ordinal);
                if (pauseIndex < 0)
                {
                    return false;
                }

                int endIndex = normalized.IndexOf(RepeatEndMarker, pauseIndex, StringComparison.Ordinal);
                if (endIndex < 0)
                {
                    return false;
                }

                string header = normalized.Substring(0, startIndex);
                string indentedBody = normalized.Substring(bodyStart, pauseIndex - bodyStart);
                string tail = normalized.Substring(endIndex + RepeatEndMarker.Length);
                string restoredBody = UnindentBody(indentedBody);

                string rebuilt = header + restoredBody + tail;
                SaveScriptNormalized(script, rebuilt);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to disable repeat for {script.FileName}: {ex}");
                GameActions.Print($"移除脚本重复逻辑失败: {ex.Message}", 33);
                return false;
            }
        }

        private void UpdateRepeatDelay(ScriptFile script, ScriptRunSettings settings)
        {
            try
            {
                string normalized = ReadScriptNormalized(script);
                int startIndex = normalized.IndexOf(RepeatStartMarker, StringComparison.Ordinal);
                if (startIndex < 0)
                {
                    return;
                }

                int pauseIndex = normalized.IndexOf("API.Pause(", startIndex, StringComparison.Ordinal);
                if (pauseIndex < 0)
                {
                    return;
                }

                int openParen = pauseIndex + "API.Pause(".Length;
                int closeParen = normalized.IndexOf(')', openParen);
                if (closeParen < 0)
                {
                    return;
                }

                string delayValue = GetDelaySeconds(settings);
                string existingValue = normalized.Substring(openParen, closeParen - openParen);
                if (existingValue == delayValue)
                {
                    return;
                }

                string updated = normalized.Substring(0, openParen) + delayValue + normalized.Substring(closeParen);

                SaveScriptNormalized(script, updated);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to update repeat delay for {script.FileName}: {ex}");
            }
        }

        private bool TryReadRepeatSettings(ScriptFile script, out int delayMs)
        {
            delayMs = 1000;
            try
            {
                string normalized = ReadScriptNormalized(script);
                int startIndex = normalized.IndexOf(RepeatStartMarker, StringComparison.Ordinal);
                if (startIndex < 0)
                {
                    return false;
                }

                int pauseIndex = normalized.IndexOf("API.Pause(", startIndex, StringComparison.Ordinal);
                if (pauseIndex < 0)
                {
                    return false;
                }

                int openParen = pauseIndex + "API.Pause(".Length;
                int closeParen = normalized.IndexOf(')', openParen);
                if (closeParen < 0)
                {
                    return false;
                }

                string delayValue = normalized.Substring(openParen, closeParen - openParen);
                if (float.TryParse(delayValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds))
                {
                    delayMs = Math.Max(1, (int)Math.Round(seconds * 1000f));
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to inspect repeat settings for {script.FileName}: {ex}");
            }

            return false;
        }

        private static void SplitScriptSections(string normalized, out string header, out string body)
        {
            int index = normalized.IndexOf("\n\n", StringComparison.Ordinal);
            if (index >= 0)
            {
                header = normalized.Substring(0, index + 2);
                body = normalized.Substring(index + 2);
            }
            else
            {
                header = "";
                body = normalized;
            }
        }

        private static string IndentBody(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return string.Empty;
            }

            string[] lines = body.Split(new[] { '\n' }, StringSplitOptions.None);
            var sb = new StringBuilder(body.Length + lines.Length * 4);

            for (int i = 0; i < lines.Length; i++)
            {
                sb.Append("    ");
                sb.Append(lines[i]);
                if (i < lines.Length - 1)
                {
                    sb.Append('\n');
                }
            }

            return sb.ToString();
        }

        private static string UnindentBody(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return string.Empty;
            }

            string[] lines = body.Split(new[] { '\n' }, StringSplitOptions.None);
            var sb = new StringBuilder(body.Length);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.StartsWith("    ", StringComparison.Ordinal))
                {
                    line = line.Substring(4);
                }
                sb.Append(line);
                if (i < lines.Length - 1)
                {
                    sb.Append('\n');
                }
            }

            return sb.ToString();
        }

        private string ReadScriptNormalized(ScriptFile script)
        {
            string text = File.ReadAllText(script.FullPath);
            return text.Replace("\r\n", "\n");
        }

        private void SaveScriptNormalized(ScriptFile script, string normalizedContent)
        {
            string converted = normalizedContent.Replace("\n", Environment.NewLine);
            script.OverrideFileContents(converted);
            script.ReadFromFile();
        }

        private static string GetDelaySeconds(ScriptRunSettings settings)
        {
            float seconds = Math.Max(1, settings.RepeatCount) / 1000f;
            return seconds.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void ToggleGroupState(bool isCollapsed, string fullGroupPath, string normalizedParentGroup, string normalizedGroupName)
        {
            if (isCollapsed)
            {
                _collapsedGroups.Remove(fullGroupPath);
                if (string.IsNullOrEmpty(normalizedParentGroup))
                    LegionScripting.LegionScripting.SetGroupCollapsed(normalizedGroupName, "", false);
                else
                    LegionScripting.LegionScripting.SetGroupCollapsed(normalizedParentGroup, normalizedGroupName, false);
            }
            else
            {
                _collapsedGroups.Add(fullGroupPath);
                if (string.IsNullOrEmpty(normalizedParentGroup))
                    LegionScripting.LegionScripting.SetGroupCollapsed(normalizedGroupName, "", true);
                else
                    LegionScripting.LegionScripting.SetGroupCollapsed(normalizedParentGroup, normalizedGroupName, true);
            }
        }

        private void PerformRename(ScriptFile script)
        {
            if (string.IsNullOrWhiteSpace(_renameState.Buffer))
            {
                _renameState.Clear();
                return;
            }

            try
            {
                // Get the original extension
                string originalExtension = Path.GetExtension(script.FileName);

                // Ensure the new name has the correct extension
                string newName = _renameState.Buffer;
                if (!newName.EndsWith(originalExtension, StringComparison.OrdinalIgnoreCase))
                {
                    newName += originalExtension;
                }

                // Build new file path
                string directory = Path.GetDirectoryName(script.FullPath);
                string newPath = Path.Combine(directory, newName);

                // Check if the new file name already exists
                if (File.Exists(newPath) && !string.Equals(script.FullPath, newPath))
                {
                    GameActions.Print(World.Instance, $"A file with the name '{newName}' already exists.", 32);
                    return;
                }

                // Perform the rename
                if (!string.Equals(script.FullPath, newPath))
                {
                    File.Move(script.FullPath, newPath);

                    // Update the script object
                    script.FullPath = newPath;
                    script.FileName = newName;

                    _pendingReload = true;
                }
            }
            catch (Exception ex)
            {
                GameActions.Print(World.Instance, $"Error renaming script: {ex.Message}", 32);
            }
            finally
            {
                _renameState.Clear();
            }
        }

        private void PerformGroupRename()
        {
            if (string.IsNullOrWhiteSpace(_renameState.Buffer))
            {
                _renameState.Clear();
                return;
            }

            try
            {
                // Build current group path
                string currentPath = LegionScripting.LegionScripting.ScriptPath;
                if (!string.IsNullOrEmpty(_renameState.GroupParent))
                    currentPath = Path.Combine(currentPath, _renameState.GroupParent);
                currentPath = Path.Combine(currentPath, _renameState.GroupName);

                // Build new group path
                string newPath = LegionScripting.LegionScripting.ScriptPath;
                if (!string.IsNullOrEmpty(_renameState.GroupParent))
                    newPath = Path.Combine(newPath, _renameState.GroupParent);
                newPath = Path.Combine(newPath, _renameState.Buffer);

                // Check if the new group name already exists
                if (Directory.Exists(newPath) && !string.Equals(currentPath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    GameActions.Print(World.Instance, $"A group with the name '{_renameState.Buffer}' already exists.", 32);
                    return;
                }

                // Check if current directory exists
                if (!Directory.Exists(currentPath))
                {
                    GameActions.Print(World.Instance, $"Source group '{_renameState.GroupName}' not found.", 32);
                    return;
                }

                // Perform the rename
                if (!string.Equals(currentPath, newPath, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Move(currentPath, newPath);
                    _pendingReload = true;
                    GameActions.Print(World.Instance, $"Renamed group '{_renameState.GroupName}' to '{_renameState.Buffer}'", 66);
                }
            }
            catch (UnauthorizedAccessException)
            {
                GameActions.Print(World.Instance, "Access denied. Check directory permissions.", 32);
            }
            catch (DirectoryNotFoundException)
            {
                GameActions.Print(World.Instance, "Directory not found.", 32);
            }
            catch (IOException ioEx)
            {
                GameActions.Print(World.Instance, $"Directory operation failed: {ioEx.Message}", 32);
            }
            catch (Exception ex)
            {
                GameActions.Print(World.Instance, $"Error renaming group: {ex.Message}", 32);
                Log.Error($"Error renaming group {_renameState.GroupName}: {ex}");
            }
            finally
            {
                _renameState.Clear();
            }
        }

        private void PerformDelete()
        {
            try
            {
                if (_dialogState.ScriptToDelete != null)
                {
                    // Delete script file
                    File.Delete(_dialogState.ScriptToDelete.FullPath);
                    LegionScripting.LegionScripting.LoadedScripts.Remove(_dialogState.ScriptToDelete);
                    GameActions.Print(World.Instance, $"Deleted script '{_dialogState.ScriptToDelete.FileName}'", 66);
                    _pendingReload = true;
                }
                else if (!string.IsNullOrEmpty(_dialogState.GroupToDelete))
                {
                    // Delete group folder
                    string gPath = string.IsNullOrEmpty(_dialogState.GroupToDeleteParent) ? _dialogState.GroupToDelete : Path.Combine(_dialogState.GroupToDeleteParent, _dialogState.GroupToDelete);
                    gPath = Path.Combine(LegionScripting.LegionScripting.ScriptPath, gPath);

                    if (Directory.Exists(gPath))
                    {
                        Directory.Delete(gPath, true);
                        GameActions.Print(World.Instance, $"Deleted group '{_dialogState.GroupToDelete}' and all its contents", 66);
                        _pendingReload = true;
                    }
                    else
                    {
                        GameActions.Print(World.Instance, $"Group '{_dialogState.GroupToDelete}' not found", 32);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                GameActions.Print(World.Instance, "Access denied. Check file/directory permissions.", 32);
            }
            catch (DirectoryNotFoundException)
            {
                GameActions.Print(World.Instance, "Directory not found.", 32);
            }
            catch (FileNotFoundException)
            {
                GameActions.Print(World.Instance, "File not found.", 32);
            }
            catch (IOException ioEx)
            {
                GameActions.Print(World.Instance, $"Delete operation failed: {ioEx.Message}", 32);
            }
            catch (Exception ex)
            {
                string itemType = _dialogState.ScriptToDelete != null ? "script" : "group";
                string itemName = _dialogState.ScriptToDelete != null ? _dialogState.ScriptToDelete.FileName : _dialogState.GroupToDelete;
                GameActions.Print(World.Instance, $"Error deleting {itemType}: {ex.Message}", 32);
                Log.Error($"Error deleting {itemType} {itemName}: {ex}");
            }
            finally
            {
                // Reset delete state using DialogState
                _dialogState.ClearAll();
            }
        }

        private void DrawContextMenus()
        {
            if (_showContextMenu)
            {
                ImGui.SetNextWindowPos(_contextMenuPosition, ImGuiCond.Appearing);
                ImGui.OpenPopup("ContextMenu");
                _showContextMenu = false;
            }

            if (ImGui.BeginPopup("ContextMenu"))
            {
                if (_contextMenuScript != null)
                {
                    DrawScriptContextMenu(_contextMenuScript);
                }
                else
                {
                    DrawGroupContextMenu(_contextMenuGroup, _contextMenuSubGroup);
                }
                ImGui.EndPopup();
            }
        }

        private void DrawScriptContextMenu(ScriptFile script)
        {
            ImGui.Text(script.FileName);
            ImGui.SeparatorText(ImGuiTranslations.Get("Options:"));

            if(ImGui.MenuItem(ImGuiTranslations.Get("Edit Constants")))
                ImGuiManager.AddWindow(new ScriptConstantsEditorWindow(script));

            if (ImGui.MenuItem(ImGuiTranslations.Get("Rename")))
            {
                // Start renaming the script
                string displayName = script.FileName;
                int lastDotIndex = displayName.LastIndexOf('.');
                if (lastDotIndex != -1)
                    displayName = displayName.Substring(0, lastDotIndex);

                _renameState.StartScriptRename(script, displayName);
                _showContextMenu = false;
            }

            if (ImGui.MenuItem(ImGuiTranslations.Get("Edit")))
            {
                ImGuiManager.AddWindow(new ScriptEditorWindow(script));
                _showContextMenu = false;
            }

            if (ImGui.MenuItem(ImGuiTranslations.Get("Edit Externally")))
            {
                OpenFileWithDefaultApp(script.FullPath);
                _showContextMenu = false;
            }

            if (ImGui.BeginMenu(ImGuiTranslations.Get("Autostart")))
            {
                bool globalAutostart = LegionScripting.LegionScripting.AutoLoadEnabled(script, true);
                bool characterAutostart = LegionScripting.LegionScripting.AutoLoadEnabled(script, false);

                if (ImGui.Checkbox(ImGuiTranslations.Get("All characters"), ref globalAutostart))
                {
                    LegionScripting.LegionScripting.SetAutoPlay(script, true, globalAutostart);
                }

                if (ImGui.Checkbox(ImGuiTranslations.Get("This character"), ref characterAutostart))
                {
                    LegionScripting.LegionScripting.SetAutoPlay(script, false, characterAutostart);
                }

                ImGui.EndMenu();
            }

            if (ImGui.MenuItem(ImGuiTranslations.Get("Create macro button")))
            {
                var mm = MacroManager.TryGetMacroManager(World.Instance);
                if (mm != null)
                {
                    var mac = new Macro(script.FileName);
                    mac.Items = new MacroObjectString(MacroType.ClientCommand, MacroSubType.MSC_NONE, "togglelscript " + script.FileName);
                    mm.PushToBack(mac);

                    var bg = new MacroButtonGump(World.Instance, mac, Mouse.Position.X, Mouse.Position.Y);
                    UIManager.Add(bg);
                }
                _showContextMenu = false;
            }

            if (ImGui.MenuItem(ImGuiTranslations.Get("Delete")))
            {
                _dialogState.ShowScriptDeleteDialog(script);
                _showContextMenu = false;
            }
        }

        private void DrawGroupContextMenu(string parentGroup, string groupName)
        {
            if (groupName != NOGROUPTEXT && !string.IsNullOrEmpty(groupName))
            {
                ImGui.Text(groupName);
                ImGui.SeparatorText(ImGuiTranslations.Get("Options:"));
                if (ImGui.MenuItem(ImGuiTranslations.Get("Rename")))
                {
                    _renameState.StartGroupRename(groupName, parentGroup);
                    _dialogState.ShowRenameGroup = true;
                    _showContextMenu = false;
                }

                if (ImGui.MenuItem(ImGuiTranslations.Get("New Script")))
                {
                    _dialogState.ShowNewScript = true;
                    _showContextMenu = false;
                }

                if (string.IsNullOrEmpty(parentGroup))
                {
                    if (ImGui.MenuItem(ImGuiTranslations.Get("New Group")))
                    {
                        _dialogState.ShowNewGroup = true;
                        _showContextMenu = false;
                    }
                }

                if (ImGui.MenuItem(ImGuiTranslations.Get("Delete Group")))
                {
                    _dialogState.ShowGroupDeleteDialog(groupName, parentGroup);
                    _showContextMenu = false;
                }
            }
            else
            {
                if (ImGui.MenuItem(ImGuiTranslations.Get("New Script")))
                {
                    _dialogState.ShowNewScript = true;
                    _showContextMenu = false;
                }

                if (string.IsNullOrEmpty(parentGroup))
                {
                    if (ImGui.MenuItem(ImGuiTranslations.Get("New Group")))
                    {
                        _dialogState.ShowNewGroup = true;
                        _showContextMenu = false;
                    }
                }
            }
        }

        private void DrawDialogs()
        {
            // Open popups when dialog state changes - ImGUI will handle positioning automatically
            if (_dialogState.ShowNewScript && !ImGui.IsPopupOpen(ImGuiTranslations.Get("New Script") + "##NewScriptDialog"))
            {
                ImGui.OpenPopup(ImGuiTranslations.Get("New Script") + "##NewScriptDialog");
                // Center the popup on the main viewport
                ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            }
            if (_dialogState.ShowNewGroup && !ImGui.IsPopupOpen(ImGuiTranslations.Get("New Group") + "##NewGroupDialog"))
            {
                ImGui.OpenPopup(ImGuiTranslations.Get("New Group") + "##NewGroupDialog");
                ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            }
            if (_dialogState.ShowRenameGroup && !ImGui.IsPopupOpen("Rename Group##RenameGroupDialog"))
            {
                ImGui.OpenPopup("Rename Group##RenameGroupDialog");
                ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            }
            if (_dialogState.ShowDeleteConfirm && !ImGui.IsPopupOpen(_dialogState.DeleteTitle))
            {
                ImGui.OpenPopup(_dialogState.DeleteTitle);
                ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            }

            // New Script Dialog
            bool showNewScript = _dialogState.ShowNewScript;
            if (ImGui.BeginPopupModal(ImGuiTranslations.Get("New Script") + "##NewScriptDialog", ref showNewScript, ImGuiWindowFlags.AlwaysAutoResize))
            {
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsWindowHovered())
                {
                    showNewScript = false;
                }

                ImGui.Text(ImGuiTranslations.Get("Enter a name for this script."));
                ImGui.Text(ImGuiTranslations.Get("Use .lscript or .py extension"));

                string scriptName = _dialogState.NewScriptName;
                ImGui.InputText("##ScriptName", ref scriptName, 100);
                _dialogState.NewScriptName = scriptName;

                ImGui.Separator();

                if (ImGui.Button(ImGuiTranslations.Get("Create") + "##CreateScript"))
                {
                    if (!string.IsNullOrEmpty(_dialogState.NewScriptName))
                    {
                        string trimmedInput = _dialogState.NewScriptName.Trim();
                        if (!trimmedInput.EndsWith(".lscript", StringComparison.OrdinalIgnoreCase) &&
                            !trimmedInput.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                        {
                            trimmedInput += ".py";
                        }

                        if (trimmedInput.EndsWith(".lscript", StringComparison.OrdinalIgnoreCase) || trimmedInput.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                        {
                            string sanitizedName = Path.GetFileName(trimmedInput);

                            if (string.IsNullOrWhiteSpace(sanitizedName) ||
                                !sanitizedName.Equals(trimmedInput, StringComparison.Ordinal) ||
                                sanitizedName.Contains("\\") ||
                                sanitizedName.Contains("/") ||
                                sanitizedName.Contains("..") ||
                                sanitizedName == "." ||
                                sanitizedName == "..")
                            {
                                GameActions.Print(World.Instance, "Invalid script name. Names cannot contain path separators or relative navigation.", 32);
                            }
                            else
                            {
                                try
                                {
                                    string normalizedGroup = _contextMenuGroup == NOGROUPTEXT ? "" : _contextMenuGroup;
                                    string normalizedSubGroup = _contextMenuSubGroup == NOGROUPTEXT ? "" : _contextMenuSubGroup;

                                    if (!string.IsNullOrEmpty(normalizedGroup))
                                        normalizedGroup = Path.GetFileName(normalizedGroup);
                                    if (!string.IsNullOrEmpty(normalizedSubGroup))
                                        normalizedSubGroup = Path.GetFileName(normalizedSubGroup);

                                    string gPath = string.IsNullOrEmpty(normalizedGroup) ? normalizedSubGroup :
                                        string.IsNullOrEmpty(normalizedSubGroup) ? normalizedGroup :
                                        Path.Combine(normalizedGroup, normalizedSubGroup);

                                    string targetDirectory = Path.Combine(LegionScripting.LegionScripting.ScriptPath, gPath ?? "");
                                    string filePath = Path.Combine(targetDirectory, sanitizedName);

                                    string scriptsRootFullPath = Path.GetFullPath(LegionScripting.LegionScripting.ScriptPath);
                                    string targetDirectoryFullPath = Path.GetFullPath(targetDirectory);
                                    string targetFileFullPath = Path.GetFullPath(filePath);

                                    if (!targetDirectoryFullPath.StartsWith(scriptsRootFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                                        !targetDirectoryFullPath.Equals(scriptsRootFullPath, StringComparison.OrdinalIgnoreCase))
                                    {
                                        GameActions.Print(World.Instance, "Invalid target directory. Path must be within the scripts directory.", 32);
                                    }
                                    else if (!targetFileFullPath.StartsWith(scriptsRootFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                                        !targetFileFullPath.Equals(scriptsRootFullPath, StringComparison.OrdinalIgnoreCase))
                                    {
                                        GameActions.Print(World.Instance, "Invalid script path. Path must be within the scripts directory.", 32);
                                    }
                                    else
                                    {
                                        if (!Directory.Exists(targetDirectoryFullPath))
                                            Directory.CreateDirectory(targetDirectoryFullPath);

                                        if (!File.Exists(targetFileFullPath))
                                        {
                                            File.WriteAllText(targetFileFullPath, SCRIPT_HEADER);
                                            RequestReload(750);
                                            GameActions.Print(World.Instance, $"Created script '{sanitizedName}'", 66);
                                        }
                                        else
                                        {
                                            GameActions.Print(World.Instance, $"A script named '{sanitizedName}' already exists.", 32);
                                        }
                                    }
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    GameActions.Print(World.Instance, "Access denied. Check directory permissions.", 32);
                                }
                                catch (DirectoryNotFoundException)
                                {
                                    GameActions.Print(World.Instance, "Directory not found.", 32);
                                }
                                catch (IOException ioEx)
                                {
                                    GameActions.Print(World.Instance, $"File operation failed: {ioEx.Message}", 32);
                                }
                                catch (Exception e)
                                {
                                    GameActions.Print(World.Instance, $"Error creating script: {e.Message}", 32);
                                    Log.Error($"Error creating script {sanitizedName}: {e}");
                                }
                            }
                        }
                        else
                        {
                            GameActions.Print(World.Instance, "Script files must end with .lscript or .py", 32);
                        }
                    }

                    _dialogState.NewScriptName = "";
                    showNewScript = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button(ImGuiTranslations.Get("Cancel") + "##CancelScript"))
                {
                    _dialogState.NewScriptName = "";
                    showNewScript = false;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            if (!showNewScript && _dialogState.ShowNewScript)
            {
                _dialogState.NewScriptName = "";
            }
            _dialogState.ShowNewScript = showNewScript;

            // New Group Dialog
            bool showNewGroup = _dialogState.ShowNewGroup;
            if (ImGui.BeginPopupModal(ImGuiTranslations.Get("New Group") + "##NewGroupDialog", ref showNewGroup, ImGuiWindowFlags.AlwaysAutoResize))
            {
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsWindowHovered())
                {
                    showNewGroup = false;
                }

                ImGui.Text(ImGuiTranslations.Get("Enter a name for this group."));

                string groupName = _dialogState.NewGroupName;
                ImGui.InputText("##GroupName", ref groupName, 100);
                _dialogState.NewGroupName = groupName;

                ImGui.Separator();

                if (ImGui.Button(ImGuiTranslations.Get("Create") + "##CreateGroup"))
                {
                    if (!string.IsNullOrEmpty(_dialogState.NewGroupName))
                    {
                        string sanitizedGroupName = Path.GetFileName(_dialogState.NewGroupName.Trim());

                        int p = sanitizedGroupName.IndexOf('.');
                        if (p != -1)
                            sanitizedGroupName = sanitizedGroupName.Substring(0, p);

                        if (string.IsNullOrEmpty(sanitizedGroupName) ||
                            !sanitizedGroupName.Equals(_dialogState.NewGroupName.Trim(), StringComparison.Ordinal) ||
                            sanitizedGroupName.Contains("\\") ||
                            sanitizedGroupName.Contains("/") ||
                            sanitizedGroupName == ".." ||
                            sanitizedGroupName == ".")
                        {
                            GameActions.Print(World.Instance, "Invalid group name. Names cannot contain path separators or relative navigation.", 32);
                        }
                        else
                        {
                            try
                            {
                                string normalizedGroup = _contextMenuGroup == NOGROUPTEXT ? "" : _contextMenuGroup;
                                string normalizedSubGroup = _contextMenuSubGroup == NOGROUPTEXT ? "" : _contextMenuSubGroup;

                                if (!string.IsNullOrEmpty(normalizedGroup))
                                    normalizedGroup = Path.GetFileName(normalizedGroup);
                                if (!string.IsNullOrEmpty(normalizedSubGroup))
                                    normalizedSubGroup = Path.GetFileName(normalizedSubGroup);

                                string path = Path.Combine(LegionScripting.LegionScripting.ScriptPath,
                                    normalizedGroup ?? "",
                                    normalizedSubGroup ?? "",
                                    sanitizedGroupName);

                                string scriptsRootPath = Path.GetFullPath(LegionScripting.LegionScripting.ScriptPath);
                                string targetPath = Path.GetFullPath(path);

                                if (!targetPath.StartsWith(scriptsRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                                    !targetPath.Equals(scriptsRootPath, StringComparison.OrdinalIgnoreCase))
                                {
                                    GameActions.Print(World.Instance, "Invalid group location. Path must be within the scripts directory.", 32);
                                }
                                else
                                {
                                    if (!Directory.Exists(targetPath))
                                    {
                                        Directory.CreateDirectory(targetPath);
                                    }
                                    File.WriteAllText(Path.Combine(targetPath, "Example.py"), EXAMPLE_LSCRIPT);
                                    _pendingReload = true;
                                    GameActions.Print(World.Instance, $"Created group '{sanitizedGroupName}'", 66);
                                }
                            }
                            catch (UnauthorizedAccessException)
                            {
                                GameActions.Print(World.Instance, "Access denied. Check directory permissions.", 32);
                            }
                            catch (DirectoryNotFoundException)
                            {
                                GameActions.Print(World.Instance, "Directory not found.", 32);
                            }
                            catch (IOException ioEx)
                            {
                                GameActions.Print(World.Instance, $"File operation failed: {ioEx.Message}", 32);
                            }
                            catch (Exception e)
                            {
                                GameActions.Print(World.Instance, $"Error creating group: {e.Message}", 32);
                                Log.Error($"Error creating group {sanitizedGroupName}: {e}");
                            }
                        }
                    }

                    _dialogState.NewGroupName = "";
                    showNewGroup = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button(ImGuiTranslations.Get("Cancel") + "##CancelGroup"))
                {
                    _dialogState.NewGroupName = "";
                    showNewGroup = false;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            if (!showNewGroup && _dialogState.ShowNewGroup)
            {
                _dialogState.NewGroupName = "";
            }
            _dialogState.ShowNewGroup = showNewGroup;

            bool showRenameGroup = _dialogState.ShowRenameGroup;
            if (ImGui.BeginPopupModal("Rename Group##RenameGroupDialog", ref showRenameGroup, ImGuiWindowFlags.AlwaysAutoResize))
            {
                _dialogState.ShowRenameGroup = showRenameGroup;
                ImGui.Text($"Enter a new name for the group '{_renameState.GroupName}'.");

                string renameBuffer = _renameState.Buffer;
                ImGui.InputText("##Group Name", ref renameBuffer, 100);
                _renameState.Buffer = renameBuffer;

                ImGui.Separator();

                if (ImGui.Button("Save"))
                {
                    if (!string.IsNullOrEmpty(_renameState.Buffer))
                    {
                        int p = _renameState.Buffer.IndexOf('.');
                        if (p != -1)
                            _renameState.Buffer = _renameState.Buffer.Substring(0, p);

                        PerformGroupRename();
                    }

                    _dialogState.ShowRenameGroup = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel"))
                {
                    _renameState.Clear();
                    _dialogState.ShowRenameGroup = false;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            // Delete Confirmation Dialog
            bool showDeleteConfirm = _dialogState.ShowDeleteConfirm;
            if (ImGui.BeginPopupModal(_dialogState.DeleteTitle, ref showDeleteConfirm, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse))
            {
                _dialogState.ShowDeleteConfirm = showDeleteConfirm;
                // Add warning icon color
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.8f, 0.0f, 1.0f)); // Orange/yellow warning color
                ImGui.Text("⚠");
                ImGui.PopStyleColor();
                ImGui.SameLine();

                ImGui.Text(_dialogState.DeleteMessage);

                ImGui.Separator();

                // Buttons with different colors
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1.0f)); // Red for delete
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.9f, 0.3f, 0.3f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.1f, 0.1f, 1.0f));

                if (ImGui.Button("Delete"))
                {
                    PerformDelete();
                    ImGui.CloseCurrentPopup();
                }

                ImGui.PopStyleColor(3);
                ImGui.SameLine();

                if (ImGui.Button("Cancel"))
                {
                    _dialogState.ClearAll();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }

        private static void OpenFileWithDefaultApp(string filePath)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    ProcessStartInfo p = new() { FileName = "xdg-open", ArgumentList = { filePath }};
                    Process.Start(p);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    ProcessStartInfo p = new() { FileName = "open", ArgumentList = { filePath }};
                    Process.Start(p);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error opening file: " + ex.Message);
            }
        }

        private void MoveScriptToGroup(ScriptFile script, string targetGroup, string targetSubGroup)
        {
            try
            {
                // Validate input parameters
                if (script == null)
                {
                    GameActions.Print(World.Instance, "Invalid script file.", 32);
                    return;
                }

                // Normalize empty strings to prevent issues
                targetGroup = targetGroup ?? "";
                targetSubGroup = targetSubGroup ?? "";

                // Prevent moving to the same location
                if (script.Group == targetGroup && script.SubGroup == targetSubGroup)
                {
                    GameActions.Print(World.Instance, "Script is already in this location.", 66);
                    return;
                }

                // Check if source file exists
                if (!File.Exists(script.FullPath))
                {
                    GameActions.Print(World.Instance, $"Source file '{script.FileName}' not found.", 32);
                    return;
                }

                // Build the target directory path
                string targetPath = LegionScripting.LegionScripting.ScriptPath;
                if (!string.IsNullOrEmpty(targetGroup))
                    targetPath = Path.Combine(targetPath, targetGroup);
                if (!string.IsNullOrEmpty(targetSubGroup))
                    targetPath = Path.Combine(targetPath, targetSubGroup);

                // Create target directory if it doesn't exist
                if (!Directory.Exists(targetPath))
                {
                    try
                    {
                        Directory.CreateDirectory(targetPath);
                    }
                    catch (Exception ex)
                    {
                        GameActions.Print(World.Instance, $"Failed to create target directory: {ex.Message}", 32);
                        return;
                    }
                }

                // Build new file path
                string newFilePath = Path.Combine(targetPath, script.FileName);

                // Check if file already exists at target location
                if (File.Exists(newFilePath))
                {
                    GameActions.Print(World.Instance, $"A file named '{script.FileName}' already exists in the target group.", 32);
                    return;
                }

                // Validate that the target path is within the scripts directory (security check)
                string normalizedTargetPath = Path.GetFullPath(targetPath);
                string normalizedScriptPath = Path.GetFullPath(LegionScripting.LegionScripting.ScriptPath);
                if (!normalizedTargetPath.StartsWith(normalizedScriptPath))
                {
                    GameActions.Print(World.Instance, "Invalid target location.", 32);
                    return;
                }

                // Check if the script is currently running and warn the user
                if (script.IsPlaying)
                {
                    GameActions.Print(World.Instance, $"Warning: Moving running script '{script.FileName}'. The script will continue running.", 34);
                }

                // Move the file
                File.Move(script.FullPath, newFilePath);

                // Remove the script from the loaded scripts collection so it gets rediscovered in its new location
                LegionScripting.LegionScripting.LoadedScripts.Remove(script);

                // Refresh the script list - this will reload scripts from files and rediscover the moved script
                _pendingReload = true;

                // Build display message for target location
                string targetDisplayName = "root";
                if (!string.IsNullOrEmpty(targetGroup))
                {
                    targetDisplayName = targetGroup;
                    if (!string.IsNullOrEmpty(targetSubGroup))
                        targetDisplayName += "/" + targetSubGroup;
                }

                GameActions.Print(World.Instance, $"Moved '{script.FileName}' to {targetDisplayName}", 66);
            }
            catch (UnauthorizedAccessException)
            {
                GameActions.Print(World.Instance, "Access denied. Check file permissions.", 32);
            }
            catch (DirectoryNotFoundException)
            {
                GameActions.Print(World.Instance, "Directory not found.", 32);
            }
            catch (IOException ioEx)
            {
                GameActions.Print(World.Instance, $"File operation failed: {ioEx.Message}", 32);
            }
            catch (Exception ex)
            {
                GameActions.Print(World.Instance, $"Error moving script: {ex.Message}", 32);
                Log.Error($"Error moving script {script.FileName}: {ex}");
            }
        }

        public override void Dispose()
        {
            _showContextMenu = false;
            _dialogState.ClearAll();
            _renameState.Clear();
            _shouldCancelRename = false;
            base.Dispose();
        }
    }
}
