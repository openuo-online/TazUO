using System.Numerics;
using ClassicUO.LegionScripting;
using ImGuiNET;

namespace ClassicUO.Game.UI.ImGuiControls.Legion;

public class ScriptEditorWindow : ImGuiWindow
{
    private readonly ScriptFile _script;
    private string _content;
    private readonly string _editorLabel;
    private bool _hasChanges;
    private const int MAX_LENGTH = 1024 * 1024;
    private bool _initialSizeSet;

    public ScriptEditorWindow(ScriptFile script) : base(script.FileName)
    {
        _script = script;
        string[] c = script.ReadFromFile();
        _content = string.Join("\n", c);
        _editorLabel = "###ScriptEditorContent" + script.FileName;

        if (_content.Length > MAX_LENGTH)
        {
            GameActions.Print("File too large to edit!", 32);
            Dispose();
        }
    }

    protected override void BeforeDraw()
    {
        if (!_initialSizeSet)
        {
            ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);
            _initialSizeSet = true;
        }
    }

    public override void DrawContent()
    {
        Vector2 availableSize = ImGui.GetContentRegionAvail();

        // Reserve space for the Save button (button height + spacing)
        float buttonHeight = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.Y;
        var editorSize = new Vector2(availableSize.X, availableSize.Y - (_hasChanges ? buttonHeight : 0));

        if (ImGui.InputTextMultiline(_editorLabel, ref _content, MAX_LENGTH, editorSize))
            _hasChanges = true;

        if (_hasChanges && ImGui.Button("Save changes"))
        {
            _hasChanges = false;
            _script.OverrideFileContents(_content);
        }
    }
}
