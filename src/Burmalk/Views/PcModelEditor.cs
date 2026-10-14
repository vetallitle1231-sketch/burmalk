using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Burmalk.Models;

namespace Burmalk.Views;

internal sealed class PcModelEditor : UserControl
{
    private readonly List<Action> _sync = new();
    private bool _updating;
    private readonly Action _changed;
    private PcModelOptions Options => App.Store.Settings.PcModel;
    public PcModelEditor(Action changed, Action showModel)
    {
        _changed = changed;
        var root = new StackPanel();
        root.Children.Add(ViewUi.Text("Собери внешний вид своего ПК. Эти настройки меняют только модель; характеристики и реальные вентиляторы остаются под управлением своих датчиков."));
        var fields = new WrapPanel { Margin = new Thickness(0, 12, 0, 6) };
        Choice(fields, "Цвет корпуса", new[] { "Белый", "Графит", "Серебристый" }, () => Array.IndexOf(new[] { "White", "Graphite", "Silver" }, Options.CaseColor), i => Options.CaseColor = new[] { "White", "Graphite", "Silver" }[i]);
        Choice(fields, "Боковая панель", new[] { "Открыта", "Стекло" }, () => Options.SidePanel == "Glass" ? 1 : 0, i => Options.SidePanel = i == 1 ? "Glass" : "Open");
        Choice(fields, "Материнская плата", new[] { "ATX", "microATX", "Mini-ITX" }, () => Array.IndexOf(new[] { "ATX", "mATX", "ITX" }, Options.BoardSize), i => Options.BoardSize = new[] { "ATX", "mATX", "ITX" }[i]);
        Choice(fields, "Вентиляторы GPU", new[] { "Встроенная графика", "1 вентилятор", "2 вентилятора", "3 вентилятора" }, () => Options.GpuFans, i => Options.GpuFans = i);
        Choice(fields, "Установка GPU", new[] { "Обычная, горизонтальная", "Вертикальная" }, () => Options.VerticalGpu ? 1 : 0, i => Options.VerticalGpu = i == 1);
        Count(fields, "Модули RAM", 4, () => Options.RamModules, i => Options.RamModules = i);
        Count(fields, "Передние вентиляторы", 3, () => Options.FrontFans, i => Options.FrontFans = i);
        Count(fields, "Задний вентилятор", 1, () => Options.RearFans, i => Options.RearFans = i);
        Count(fields, "Верхние вентиляторы · воздух", 3, () => Options.TopFans, i => Options.TopFans = i);
        Count(fields, "Нижние вентиляторы", 3, () => Options.BottomFans, i => Options.BottomFans = i);
        Choice(fields, "Вентиляторы воздушного кулера", new[] { "1 вентилятор", "2 вентилятора" }, () => Options.AirCoolerFans - 1, i => Options.AirCoolerFans = i + 1);
        Choice(fields, "Радиатор СЖО", new[] { "120 мм · 1 вентилятор", "240 мм · 2 вентилятора", "360 мм · 3 вентилятора" }, () => Options.RadiatorFans - 1, i => Options.RadiatorFans = i + 1);
        Count(fields, "Накопители в модели", 4, () => Options.Drives, i => Options.Drives = i);
        Choice(fields, "Подсветка", new[] { "Без подсветки", "Бирюзовая", "Фиолетовая", "Тёплая" }, () => Array.IndexOf(new[] { "Off", "Teal", "Purple", "Warm" }, Options.Lighting), i => Options.Lighting = new[] { "Off", "Teal", "Purple", "Warm" }[i]);
        root.Children.Add(fields);
        var actions = new WrapPanel();
        actions.Children.Add(ViewUi.Button("К 3D-модели ↑", showModel));
        actions.Children.Add(ViewUi.Button("Собрать ПК целиком", ApplyCompleteBuild));
        actions.Children.Add(ViewUi.Button("Белый корпус · сброс модели", () => { App.Store.Settings.PcModel = new PcModelOptions(); Commit(); Synchronize(); }));
        actions.Children.Add(ViewUi.Button("Графит + бирюза", () => { Options.CaseColor = "Graphite"; Options.Lighting = "Teal"; Commit(); Synchronize(); }));
        root.Children.Add(actions);
        root.Children.Add(ViewUi.Text("Изменения сохраняются сразу и применяются к 3D-виджету на главной. Тип корпуса и охлаждения выбери над моделью. На ноутбуке используется отдельная модель."));
        Content = root; Synchronize();
    }
    private void Count(Panel parent, string label, int max, Func<int> get, Action<int> set)
    { var values = new string[max + 1]; for (int i = 0; i <= max; i++) values[i] = i.ToString(); Choice(parent, label, values, get, set); }
    private void Choice(Panel parent, string label, string[] values, Func<int> get, Action<int> set)
    {
        var field = new StackPanel { Width = 224, Margin = new Thickness(0, 0, 16, 14) };
        var title = ViewUi.Text(label); title.FontSize = 12; title.Margin = new Thickness(0, 0, 0, 6); field.Children.Add(title);
        var choice = new ComboBox { ItemsSource = values, MinHeight = 34, SelectedIndex = Math.Clamp(get(), 0, values.Length - 1) }; AutomationProperties.SetName(choice, label); field.Children.Add(choice); parent.Children.Add(field);
        _sync.Add(() => choice.SelectedIndex = Math.Clamp(get(), 0, values.Length - 1));
        choice.SelectionChanged += (s, e) => { if (_updating || choice.SelectedIndex < 0) return; set(choice.SelectedIndex); Commit(); };
    }
    private void Commit() { Options.Normalize(); App.Store.Save(); _changed(); }
    internal void ApplyCompleteBuild()
    {
        App.Store.Settings.PcModel = PcModelOptions.CompleteTower();
        App.Store.Settings.PcModelChassis = "Desktop";
        if (App.Store.Settings.PcModelCooling != "Aio") App.Store.Settings.PcModelCooling = "Air";
        Commit(); Synchronize();
    }
    internal void Synchronize() { _updating = true; try { foreach (var sync in _sync) sync(); } finally { _updating = false; } }
}
