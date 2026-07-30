using SteamSwitcher.Core.Models;
using System.Windows;
using System.Windows.Input;

namespace SteamSwitcher.Views.Dialogs;

public partial class HotkeyCaptureDialog : Window
{
    private readonly HashSet<Key> _pressedKeys = [];

    private Key _capturedMainKey = Key.None;
    private HotkeyDefinition? _pendingHotkey;

    public bool IsCapturingHotkey { get; private set; } = true;

    public HotkeyDefinition? CapturedHotkey { get; private set; }

    public HotkeyCaptureDialog()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            Focus();
            Keyboard.Focus(this);
        };
    }

    private void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsCapturingHotkey)
            return;

        var key = NormalizeKey(e.Key == Key.System ? e.SystemKey : e.Key);

        // Ignora KeyDown repetido por auto-repeat.
        if (!_pressedKeys.Add(key))
        {
            e.Handled = true;
            return;
        }

        if (IsModifier(key))
        {
            UpdateModifierPreview();
            e.Handled = true;
            return;
        }

        var modifiers = GetPressedModifiers();

        // Não aceita tecla principal sem modificador.
        if (modifiers == HotkeyModifiers.None)
        {
            HintText.Text = "Inclua Ctrl, Shift, Alt ou Win.";
            e.Handled = true;
            return;
        }

        _capturedMainKey = key;
        _pendingHotkey = new HotkeyDefinition
        {
            Modifiers = modifiers,
            VirtualKey = KeyInterop.VirtualKeyFromKey(key),
            KeyName = key.ToString()
        };

        HotkeyText.Text = _pendingHotkey.DisplayText;
        HintText.Text = "Solte a tecla principal para confirmar a captura.";
        e.Handled = true;
    }

    private void Dialog_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = NormalizeKey(e.Key == Key.System ? e.SystemKey : e.Key);
        _pressedKeys.Remove(key);

        // Só finaliza quando a tecla principal é solta.
        if (key == _capturedMainKey && _pendingHotkey is not null)
        {
            CapturedHotkey = _pendingHotkey;
            IsCapturingHotkey = false;

            HotkeyText.Text = CapturedHotkey.DisplayText;
            HintText.Text = "Atalho definido. Clique em “Usar atalho”.";
            ConfirmButton.IsEnabled = true;

            _capturedMainKey = Key.None;
            _pendingHotkey = null;
        }
        else if (IsCapturingHotkey)
        {
            UpdateModifierPreview();
        }

        e.Handled = true;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (CapturedHotkey?.IsValid != true)
            return;

        DialogResult = true;
        Close();
    }

    private void UpdateModifierPreview()
    {
        var modifiers = GetPressedModifiers();

        HotkeyText.Text = modifiers switch
        {
            HotkeyModifiers.None => "Aguardando atalho...",
            _ => FormatModifiers(modifiers) + " + ..."
        };

        HintText.Text = "Pressione uma tecla principal para concluir.";
    }

    private HotkeyModifiers GetPressedModifiers()
    {
        var modifiers = HotkeyModifiers.None;

        if (_pressedKeys.Contains(Key.LeftCtrl))
            modifiers |= HotkeyModifiers.Ctrl;

        if (_pressedKeys.Contains(Key.LeftShift))
            modifiers |= HotkeyModifiers.Shift;

        if (_pressedKeys.Contains(Key.LeftAlt))
            modifiers |= HotkeyModifiers.Alt;

        if (_pressedKeys.Contains(Key.LWin))
            modifiers |= HotkeyModifiers.Win;

        return modifiers;
    }

    private static bool IsModifier(Key key) =>
        key is Key.LeftCtrl or Key.LeftShift or Key.LeftAlt or Key.LWin;

    private static Key NormalizeKey(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => Key.LeftCtrl,
        Key.LeftShift or Key.RightShift => Key.LeftShift,
        Key.LeftAlt or Key.RightAlt => Key.LeftAlt,
        Key.LWin or Key.RWin => Key.LWin,
        _ => key
    };

    private static string FormatModifiers(HotkeyModifiers modifiers)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");

        return string.Join(" + ", parts);
    }
}