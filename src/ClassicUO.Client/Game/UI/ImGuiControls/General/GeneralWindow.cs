using System;
using ImGuiNET;
using ClassicUO.Configuration;
using ClassicUO.Network;

namespace ClassicUO.Game.UI.ImGuiControls
{
    public class GeneralWindow : SingletonImGuiWindow<GeneralWindow>
    {
        private readonly Profile _profile = ProfileManager.CurrentProfile;
        private int _objectMoveDelay;
        private bool _highlightObjects;
        private bool _showNames;
        private bool _autoOpenOwnCorpse;
        private ushort _turnDelay;
        private float _imguiWindowAlpha, _lastImguiWindowAlpha;
        private int _currentThemeIndex;
        private string[] _themeNames;
        private GeneralWindow() : base(ImGuiTranslations.Get("General"))
        {
            WindowFlags = ImGuiWindowFlags.AlwaysAutoResize;
            _objectMoveDelay = _profile.MoveMultiObjectDelay;
            _highlightObjects = _profile.HighlightGameObjects;
            _showNames = _profile.NameOverheadToggled;
            _autoOpenOwnCorpse = _profile.AutoOpenOwnCorpse;
            _turnDelay = _profile.TurnDelay;
            _imguiWindowAlpha = _lastImguiWindowAlpha = Client.Settings.Get(SettingsScope.Global, Constants.SqlSettings.IMGUI_ALPHA, 1.0f);

            // Initialize theme selector
            _themeNames = ImGuiTheme.GetThemes();
            string currentTheme = Client.Settings.Get(SettingsScope.Global, Constants.SqlSettings.IMGUI_THEME, "Default");
            _currentThemeIndex = Array.IndexOf(_themeNames, currentTheme);
            if (_currentThemeIndex < 0) _currentThemeIndex = 0;
        }

        public override void DrawContent()
        {
            if (_profile == null)
            {
                ImGui.Text(ImGuiTranslations.Get("Profile not loaded"));
                return;
            }

            ImGui.Spacing();

            if (ImGui.BeginTabBar("##GeneralTabs", ImGuiTabBarFlags.None))
            {
                if (ImGui.BeginTabItem(ImGuiTranslations.Get("Options")))
                {
                    DrawOptionsTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(ImGuiTranslations.Get("Info")))
                {
                    DrawInfoTab();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(ImGuiTranslations.Get("HUD")))
                {
                    HudWindow.GetInstance()?.DrawContent();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(ImGuiTranslations.Get("Spell Bar")))
                {
                    SpellBarWindow.GetInstance()?.DrawContent();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(ImGuiTranslations.Get("Title Bar")))
                {
                    TitleBarWindow.GetInstance()?.DrawContent();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(ImGuiTranslations.Get("Spell Indicators")))
                {
                    SpellIndicatorWindow.GetInstance()?.DrawContent();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(ImGuiTranslations.Get("Friends List")))
                {
                    FriendsListWindow.GetInstance()?.DrawContent();
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }


        private void DrawOptionsTab()
        {
            // Group: Visual Config
            ImGui.BeginGroup();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ImGuiTheme.Current.BaseContent, ImGuiTranslations.Get("Visual Config"));

            ImGui.SetNextItemWidth(125);
            if (ImGui.SliderFloat(ImGuiTranslations.Get("Assistant Alpha"), ref _imguiWindowAlpha, 0.2f, 1.0f, "%.2f"))
            {
                if(Math.Abs(_imguiWindowAlpha - _lastImguiWindowAlpha) > 0.05)
                {
                    _imguiWindowAlpha = Math.Clamp(_imguiWindowAlpha, 0.2f, 1.0f);
                    _ = Client.Settings.SetAsync(SettingsScope.Global, Constants.SqlSettings.IMGUI_ALPHA, _imguiWindowAlpha);
                    ImGuiManager.UpdateTheme(_imguiWindowAlpha);
                    _lastImguiWindowAlpha = _imguiWindowAlpha;
                }
            }
            ImGuiComponents.Tooltip(ImGuiTranslations.Get("Adjust the background transparency of all ImGui windows."));

            ImGui.SetNextItemWidth(125);
            if (ImGui.Combo(ImGuiTranslations.Get("Theme"), ref _currentThemeIndex, _themeNames, _themeNames.Length))
            {
                string selectedTheme = _themeNames[_currentThemeIndex];
                if (ImGuiTheme.SetTheme(selectedTheme))
                {
                    _ = Client.Settings.SetAsync(SettingsScope.Global, Constants.SqlSettings.IMGUI_THEME, selectedTheme);
                    ImGuiManager.UpdateTheme(_imguiWindowAlpha);
                }
            }
            ImGuiComponents.Tooltip(ImGuiTranslations.Get("Select the color theme for ImGui windows."));

            ImGui.SetNextItemWidth(125);
            if (ImGui.SliderFloat(ImGuiTranslations.Get("Assistant Alpha"), ref _imguiWindowAlpha, 0.2f, 1.0f, "%.2f"))
            {
                if(Math.Abs(_imguiWindowAlpha - _lastImguiWindowAlpha) > 0.05)
                {
                    _imguiWindowAlpha = Math.Clamp(_imguiWindowAlpha, 0.2f, 1.0f);
                    _ = Client.Settings.SetAsync(SettingsScope.Global, Constants.SqlSettings.IMGUI_ALPHA, _imguiWindowAlpha);
                    ImGuiManager.UpdateTheme(_imguiWindowAlpha);
                    _lastImguiWindowAlpha = _imguiWindowAlpha;
                }
            }
            ImGuiComponents.Tooltip(ImGuiTranslations.Get("Adjust the background transparency of all ImGui windows."));

            if (ImGui.Checkbox(ImGuiTranslations.Get("Highlight game objects"), ref _highlightObjects))
            {
                _profile.HighlightGameObjects = _highlightObjects;
            }

            if (ImGui.Checkbox(ImGuiTranslations.Get("Show Names"), ref _showNames))
            {
                _profile.NameOverheadToggled = _showNames;
            }
            ImGuiComponents.Tooltip(ImGuiTranslations.Get("Toggle the display of names above characters and NPCs in the game world."));

            if (ImGui.Checkbox(ImGuiTranslations.Get("Auto open own corpse"), ref _autoOpenOwnCorpse))
            {
                _profile.AutoOpenOwnCorpse = _autoOpenOwnCorpse;
            }
            ImGuiComponents.Tooltip(ImGuiTranslations.Get("Automatically open your own corpse when you die, even if auto open corpses is disabled."));

            ImGui.EndGroup();

            ImGui.SameLine();

            // Group: Delay Config
            ImGui.BeginGroup();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(ImGuiTheme.Current.BaseContent, ImGuiTranslations.Get("Delay Config"));

            int tempTurnDelay = _turnDelay;

            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderInt(ImGuiTranslations.Get("Turn Delay"), ref tempTurnDelay, 0, 150, " %d ms"))
            {
                if (tempTurnDelay < 0) tempTurnDelay = 0;
                if (tempTurnDelay > ushort.MaxValue) tempTurnDelay = 100;

                _turnDelay = (ushort)tempTurnDelay;
                _profile.TurnDelay = _turnDelay;
            }
            ImGui.SetNextItemWidth(150);
            if (ImGui.InputInt(ImGuiTranslations.Get("Object Delay"), ref _objectMoveDelay, 50, 100))
            {
                _objectMoveDelay = Math.Clamp(_objectMoveDelay, 0, 3000);

                _profile.MoveMultiObjectDelay = _objectMoveDelay;
            }
            ImGui.EndGroup();
        }

        private readonly string _version = ImGuiTranslations.Get("TazUO Version: ") + CUOEnviroment.Version; //Pre-cache to prevent reading var and string concatenation every frame
        private uint _lastObject = 0;
        private string _lastObjectString = ImGuiTranslations.Get("Last Object: ") + "0x00000000";
        private void DrawInfoTab()
        {
            if (World.Instance != null)
            {
                if (_lastObject != World.Instance.LastObject)
                {
                    _lastObject = World.Instance.LastObject;
                    _lastObjectString = ImGuiTranslations.Get("Last Object: ") + $"0x{_lastObject:X8}";
                }
            }

            ImGui.Text(ImGuiTranslations.Get("Ping: ") + AsyncNetClient.Socket.Statistics.Ping + "ms");
            ImGui.Spacing();
            ImGui.Text(ImGuiTranslations.Get("FPS: ") + CUOEnviroment.CurrentRefreshRate);
            ImGui.Spacing();
            ImGui.Text(_lastObjectString);
            if(ImGui.IsItemClicked())
            {
                SDL3.SDL.SDL_SetClipboardText($"0x{_lastObject:X8}");
                GameActions.Print(ImGuiTranslations.Get("Copied last object to clipboard."), 62);
            }
            ImGui.Spacing();
            ImGui.Text(_version);
        }

    }
}
