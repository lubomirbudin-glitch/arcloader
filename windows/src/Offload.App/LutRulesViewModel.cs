using System.Collections.ObjectModel;
using System.IO;
using Offload.Core;

namespace Offload.App;

/// "Vlastní pravidla LUTů" (spec 4.2): a copy of the rules, edited row by row; OK hands back the new list.
/// A rule of a newer version (ruling I3) is a read-only row: it keeps its number, can be moved or removed, and goes back
/// unchanged.
public sealed class LutRulesViewModel : ObservableObject
{
    string? problem;

    public LutRulesViewModel(IReadOnlyList<LutRule> rules, LutCatalog catalog)
    {
        LutChoices = [.. catalog.Luts.Select(l => new Option<string>(l.Id, l.Name)), new("none", "Bez LUT"), new(Custom, "Vlastní…")];
        foreach (var r in rules) Rows.Add(new LutRuleRowViewModel(this, r));
        AddCommand = new RelayCommand(() =>
        {
            Rows.Add(new LutRuleRowViewModel(this, new LutRule { Match = LutRule.Camera, Lut = "none" }));
            Problem = null;
        });
    }

    public const string Custom = "custom";

    public ObservableCollection<LutRuleRowViewModel> Rows { get; } = [];
    /// Built-in LUTs, "Bez LUT", "Vlastní…" — the same instances for every row (ComboBox compares by reference).
    public IReadOnlyList<Option<string>> LutChoices { get; }
    public RelayCommand AddCommand { get; }

    /// Why OK cannot take the rules (red), or null.
    public string? Problem
    {
        get => problem;
        set => Set(ref problem, value);
    }

    /// The rules in the rows' order, or null (and Problem says why) when a row is not complete.
    public List<LutRule>? Result()
    {
        var rules = Rows.Select(r => r.ToRule()).ToList();
        Problem = LutRules.Problem(rules);
        return Problem == null ? LutRules.Valid(rules) : null;
    }

    internal void Move(LutRuleRowViewModel row, int by)
    {
        int i = Rows.IndexOf(row), j = i + by;
        if (i < 0 || j < 0 || j >= Rows.Count) return;
        Rows.Move(i, j);
    }

    internal void Remove(LutRuleRowViewModel row)
    {
        Rows.Remove(row);
        Problem = null;
    }
}

/// One rule: [Podle ▾] [hodnota] → [LUT ▾] [↑] [↓] [✕] — or, for a rule of a newer version, a read-only line.
public sealed class LutRuleRowViewModel : ObservableObject
{
    readonly LutRulesViewModel owner;
    readonly LutRule original;
    readonly Dictionary<string, System.Text.Json.JsonElement>? extra;
    Option<string> kind;
    string value;
    string? lut;
    string? lutPath;

    public static IReadOnlyList<Option<string>> Kinds { get; } =
    [
        new(LutRule.Camera, "Kamera (výrobce/model)"),
        new(LutRule.Serial, "Sériové číslo kamery"),
        new(LutRule.Reel, "Cívka (začátek)"),
        new(LutRule.Folder, "Složka (vzor s * a ?)"),
    ];

    public LutRuleRowViewModel(LutRulesViewModel owner, LutRule rule)
    {
        this.owner = owner;
        original = rule;
        kind = Kinds.FirstOrDefault(k => k.Value == rule.Match) ?? Kinds[0];
        value = rule.Value;
        lut = rule.Lut;
        lutPath = rule.LutPath;
        extra = rule.Extra;
        UpCommand = new RelayCommand(() => owner.Move(this, -1));
        DownCommand = new RelayCommand(() => owner.Move(this, +1));
        RemoveCommand = new RelayCommand(() => owner.Remove(this));
    }

    /// Ruling I3: a match this version does not know (or an item that is not a rule at all) — shown, never edited.
    public bool IsFuture => LutRules.IsFuture(original);
    public bool IsEditable => !IsFuture;
    /// "Pravidlo z novější verze (lens) — tato verze ho přeskočí"
    public string FutureText => $"Pravidlo z novější verze ({(original.Raw == null || original.Match != "?" ? original.Match : "neznámé")}) — tato verze ho přeskočí";

    public IReadOnlyList<Option<string>> LutChoices => owner.LutChoices;
    public RelayCommand UpCommand { get; }
    public RelayCommand DownCommand { get; }
    public RelayCommand RemoveCommand { get; }

    public Option<string> Kind
    {
        get => kind;
        set { if (value != null) Set(ref kind, value); }
    }

    public string Value
    {
        get => value;
        set => Set(ref this.value, value ?? "");
    }

    /// The chosen LUT; "Vlastní…" opens the .cube dialog (deferred: not inside the ComboBox's own change), a cancel
    /// keeps the earlier choice.
    public Option<string> Lut
    {
        get => LutChoices.FirstOrDefault(c => c.Value == (lutPath != null ? LutRulesViewModel.Custom : lut ?? "none"))
               ?? LutChoices.First(c => c.Value == "none");
        set
        {
            if (value == null) return;
            if (value.Value == LutRulesViewModel.Custom)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (Dialogs.PickFile("Vyber LUT (.cube)", "LUT (*.cube)|*.cube") is string path)
                    {
                        lutPath = path;
                        lut = null;
                    }
                    RaiseMany(nameof(Lut), nameof(CustomName), nameof(CustomPath));
                });
                return;
            }
            lut = value.Value;
            lutPath = null;
            RaiseMany(nameof(Lut), nameof(CustomName), nameof(CustomPath));
        }
    }

    /// The own .cube's file name next to the choice (the full path in its tooltip), or null.
    public string? CustomName => lutPath != null ? Path.GetFileName(lutPath) : null;
    public string? CustomPath => lutPath;

    /// What the row stands for: a rule of a newer version goes back as it was read (never rewritten as "camera").
    public LutRule ToRule() => IsFuture
        ? original
        : new() { Match = kind.Value, Value = value.Trim(), Lut = lutPath == null ? lut : null, LutPath = lutPath, Extra = extra };
}
