using System.ComponentModel;
using GestionLibros.Data;
using GestionLibros.Models;
namespace GestionLibros.Views;

// In-page floating window: keeps Consulta and its locator/selection behind the detail.
internal sealed class AccountMovementsDialog : Grid
{
 private static readonly string[] Headers = ["TPol", "Ref.", "Fecha", "Cuenta", "Concepto", "Cargos", "Créditos"];
 private static readonly int[] Widths = [36, 56, 48, 58, 195, 100, 100];
 private readonly Page owner;
 private readonly AppDatabase db;
 private readonly Company company;
 private readonly CollectionView table = new() { SelectionMode = SelectionMode.Single, BackgroundColor = Colors.Transparent };
 private readonly Label accountTitle = ClassicWorkspaceChrome.Text("", true);
 private readonly Label status = ClassicWorkspaceChrome.Text("");
 private readonly Label summary = ClassicWorkspaceChrome.Text("", true);
 private readonly Label debit = ClassicWorkspaceChrome.Text("", true), credit = ClassicWorkspaceChrome.Text("", true);
 private readonly Border window;
 private readonly Grid body;
 private readonly View open;
 private List<MovementItem> items = [];
 private MovementItem? selected;
 private Account? account;
 private int year, month, generation;
 private bool opening;
 private AppDatabase.ConsultationSource source;
 public Action? Closed { get; set; }

 public AccountMovementsDialog(Page owner, AppDatabase db, Company company)
 {
  this.owner = owner; this.db = db; this.company = company;
  IsVisible = false; BackgroundColor = Color.FromArgb("#01000000");
  var header = Columns(); header.HeightRequest = 18;
  for (int i = 0; i < Headers.Length; i++)
  {
   var label = ClassicWorkspaceChrome.Text(Headers[i], true); label.HorizontalTextAlignment = TextAlignment.Center;
   header.Add(new Border { Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, Background = ClassicWorkspaceChrome.ToolbarBrush(), Content = label }, i, 0);
  }
  table.ItemTemplate = new DataTemplate(() =>
  {
   var row = Columns(); row.HeightRequest = 14;
   row.SetBinding(BackgroundColorProperty, nameof(MovementItem.Background));
   for (int i = 0; i < Headers.Length; i++)
   {
    var label = ClassicWorkspaceChrome.Text(""); label.Padding = new Thickness(3, 0);
    label.LineBreakMode = LineBreakMode.NoWrap;
    label.SetBinding(Label.TextProperty, $"Cells[{i}]"); label.SetBinding(Label.TextColorProperty, nameof(MovementItem.Foreground));
    if (i >= 5) label.HorizontalTextAlignment = TextAlignment.End;
    // An amount or reference opens the precise persisted journal and highlights this movement.
    if (i == 1 || i >= 5)
    {
     var click = new TapGestureRecognizer();
     click.Tapped += async (_, _) => { if (row.BindingContext is MovementItem item) { table.SelectedItem = item; await OpenJournal(item); } };
     label.GestureRecognizers.Add(click);
    }
    row.Add(label, i, 0);
   }
   var doubleClick = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
   doubleClick.Tapped += async (_, _) => { if (row.BindingContext is MovementItem item) await OpenJournal(item); };
   row.GestureRecognizers.Add(doubleClick); return row;
  });
  table.SelectionChanged += (_, e) =>
  {
   selected?.Select(false); selected = e.CurrentSelection.FirstOrDefault() as MovementItem; selected?.Select(true);
   if (open != null) open.IsEnabled = selected != null;
   if (selected != null) status.Text = selected.Row.IsImported ? "Acumulado GL2000 sin partidas diarias. " + selected.Row.Origin : selected.Row.Origin;
  };
  ClassicWorkspaceChrome.CompactRows(table);
  CaptureShortcut.Enter(table, () => selected == null ? Task.CompletedTask : OpenJournal(selected));
  var ruled = Columns(); ruled.BackgroundColor = Gl2000Chrome.RowYellow;
  for (var i = 0; i < Widths.Length; i++) ruled.Add(new BoxView { WidthRequest = 1, HorizontalOptions = LayoutOptions.End, Color = Colors.Gray, InputTransparent = true }, i, 0);
  ruled.Add(table); Grid.SetColumnSpan(table, Widths.Length + 1);
  var totals = Columns(); totals.HeightRequest = 22; totals.BackgroundColor = Colors.White;
  var labelTotal = ClassicWorkspaceChrome.Text("Totales :", true); labelTotal.HorizontalTextAlignment = TextAlignment.End;
  totals.Add(labelTotal, 4, 0); totals.Add(debit, 5, 0); totals.Add(credit, 6, 0);
  debit.HorizontalTextAlignment = credit.HorizontalTextAlignment = TextAlignment.End;
  debit.Padding = credit.Padding = new Thickness(3, 0);
  var data = new Grid { RowSpacing = 0, MinimumWidthRequest = Widths.Sum() + 18, RowDefinitions = { new(18), new(GridLength.Star), new(22) } };
  data.Add(header, 0, 0); data.Add(ruled, 0, 1); data.Add(totals, 0, 2);
  var scroll = new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = data, HorizontalScrollBarVisibility = ScrollBarVisibility.Always };
  scroll.SizeChanged += (_, _) => { if (scroll.Width > 0) data.WidthRequest = Math.Max(Widths.Sum() + 18, scroll.Width); };
  var nav = new HorizontalStackLayout { Spacing = 1 };
  foreach (var (text, step) in new[] { ("|◀", int.MinValue), ("◀", -1), ("▶", 1), ("▶|", int.MaxValue) })
   nav.Children.Add(JournalsPage.FlatButton(text, () => { Move(step); return Task.CompletedTask; }, 24));
  nav.Children.Add(summary);
  var panel = new Grid { Padding = 5, BackgroundColor = Gl2000Chrome.HeaderBlue, RowDefinitions = { new(24), new(GridLength.Star), new(24), new(30) } };
  panel.Add(accountTitle, 0, 0); panel.Add(scroll, 0, 1); panel.Add(nav, 0, 2); panel.Add(status, 0, 3);
  status.LineBreakMode = LineBreakMode.WordWrap; status.TextColor = Colors.DarkBlue;
  summary.Margin = new Thickness(8, 0);
  var footer = new HorizontalStackLayout { HorizontalOptions = LayoutOptions.End, Spacing = 5, Padding = new Thickness(0, 4) };
  open = JournalsPage.ActionButton("Ver partida", "gl_policy.png", () => selected == null ? Task.CompletedTask : OpenJournal(selected), 100); open.IsEnabled = false;
  footer.Children.Add(open);
  footer.Children.Add(JournalsPage.ActionButton("Salir", "gl_close.png", () => { Close(); return Task.CompletedTask; }, 84));
  footer.Children.Add(JournalsPage.ActionButton("Ayuda", "gl_help.png", Help, 84));
  var tab = new Border { HorizontalOptions = LayoutOptions.Start, Padding = new Thickness(5, 0), Stroke = Colors.Gray, StrokeThickness = 1, BackgroundColor = Gl2000Chrome.HeaderBlue, Content = ClassicWorkspaceChrome.Text("Cuenta", true) };
  body = new Grid { Padding = 6, RowSpacing = 0, RowDefinitions = { new(22), new(GridLength.Star), new(32) } };
  body.Add(tab, 0, 0); body.Add(panel, 0, 1); body.Add(footer, 0, 2);
  window = ClassicDialog.ChildWindow(ClassicWorkspaceChrome.Text("Desglose de Movimientos"), Gl2000Chrome.Backdrop(body, "gl_card.png", 33, 31), 930, Close);
  Children.Add(window);
  SizeChanged += (_, _) => Resize();
 }
 private void Resize()
 {
  if (Width <= 0 || Height <= 0) return;
  window.WidthRequest = Math.Max(1, Math.Min(930, Width - 16));
  body.HeightRequest = Math.Max(180, Math.Min(620, Height - 50));
 }
 private static Grid Columns()
 {
  var grid = new Grid { ColumnSpacing = 0, RowSpacing = 0 };
  foreach (var width in Widths) grid.ColumnDefinitions.Add(new(width));
  grid.ColumnDefinitions.Add(new(GridLength.Star)); return grid;
 }
 public async Task Show(Account value, int year, int month, AppDatabase.ConsultationSource source = AppDatabase.ConsultationSource.Reconciled)
 {
  this.source = source;
  account = value; this.year = year; this.month = month;
  IsVisible = true; window.TranslationX = window.TranslationY = 0; Resize();
  await Refresh();
 }
 public async Task Refresh()
 {
  if (!IsVisible || account == null) return;
  var request = ++generation;
  var keep = selected?.Row.LineId;
  items = []; table.ItemsSource = items; selected = null; open.IsEnabled = false;
  debit.Text = credit.Text = summary.Text = ""; status.Text = "Consultando movimientos…";
  accountTitle.Text = $"Cuenta: {account.Code} — {account.Description} · {month:00}/{year}";
  try
  {
   var start = new DateTime(year, month, 1);
   var report = await db.Ledger(company.Id, account.Id, start, start.AddMonths(1).AddDays(-1), source: source);
   if (request != generation || !IsVisible) return;
   items = report.Rows.Select(r => new MovementItem(r)).ToList(); table.ItemsSource = items;
   debit.Text = AccountingUi.Money(report.Debits); credit.Text = AccountingUi.Money(report.Credits);
   summary.Text = $"{items.Count} movimientos · Inicial: {AccountingUi.Money(report.Opening)} · Final: {AccountingUi.Money(report.Closing)}";
   table.SelectedItem = items.FirstOrDefault(i => keep > 0 && i.Row.LineId == keep) ?? items.FirstOrDefault();
   status.Text = items.Count == 0 ? "No hay movimientos en este mes. El saldo inicial corresponde a períodos anteriores." : "Pulse un cargo, crédito o referencia para consultar la partida completa.";
   if (items.Any(i => i.Row.IsImported)) status.Text = "Incluye acumulados GL2000: esas filas no tienen partidas individuales disponibles.";
   if (source != AppDatabase.ConsultationSource.Reconciled)
       accountTitle.Text += source == AppDatabase.ConsultationSource.ImportedOnly ? " · Solo GL2000 (corte pendiente)" : " · Solo FREDI (corte pendiente)";
  }
  catch (Exception ex) { if (request == generation) status.Text = ex is InvalidOperationException ? ex.Message : "No se pudo cargar el desglose. Cierre y vuelva a consultar."; }
 }
 private void Move(int step)
 {
  if (items.Count == 0) return;
  var i = selected == null ? 0 : items.IndexOf(selected);
  var target = step == int.MinValue ? 0 : step == int.MaxValue ? items.Count - 1 : Math.Clamp(i + step, 0, items.Count - 1);
  table.SelectedItem = items[target]; table.ScrollTo(items[target], position: ScrollToPosition.MakeVisible, animate: false);
 }
 private async Task OpenJournal(MovementItem item)
 {
  if (opening || owner.Navigation.NavigationStack.LastOrDefault() != owner) return;
  opening = true;
  try
  {
   db.ValidateSession(true);
   if (item.Row.IsImported || item.Row.JournalId <= 0 || item.Row.LineId <= 0)
   {
    await owner.DisplayAlertAsync("Acumulado sin detalle", "GL2000 solo aporta el total mensual de esta fila. No existe una partida individual que abrir. Importe y concilie el detalle histórico para consultarla.\n" + item.Row.Origin, "Cerrar"); return;
   }
   var journal = await db.JournalForMovement(company.Id, item.Row.JournalId, item.Row.LineId);
   await owner.Navigation.PushAsync(new JournalEditor(db, company, journal, viewOnly: true, selectedLineId: item.Row.LineId));
  }
  catch (Exception ex) { status.Text = ex is InvalidOperationException ? ex.Message : "No se pudo abrir la partida."; }
  finally { opening = false; }
 }
 public void Close() { generation++; IsVisible = false; Closed?.Invoke(); }
 private Task Help() => owner.DisplayAlertAsync("Desglose de Movimientos", "Muestra los movimientos del mes seleccionado, con las subcuentas cuando corresponda. Pulse un importe o referencia para abrir la partida completa en modo consulta, resaltando el movimiento. Volver regresa al desglose. Los acumulados sin detalle no se presentan como partidas individuales.", "Cerrar");
 private sealed class MovementItem(LedgerRow row) : INotifyPropertyChanged
 {
  public LedgerRow Row { get; } = row;
  public string[] Cells { get; } = [row.Type, row.IsImported ? "GL2000" : row.Reference, row.Date.ToString(row.IsImported ? "MM/yyyy" : "dd/MM/yyyy"), row.AccountCode, row.Concept, row.Debits == 0 ? "" : AccountingUi.Money(row.Debits), row.Credits == 0 ? "" : AccountingUi.Money(row.Credits)];
  public Color Background { get; private set; } = Colors.Transparent;
  public Color Foreground { get; private set; } = Colors.Black;
  public event PropertyChangedEventHandler? PropertyChanged;
  public void Select(bool selected)
  {
   Background = selected ? Color.FromArgb("#0078D7") : Colors.Transparent; Foreground = selected ? Colors.White : Colors.Black;
   PropertyChanged?.Invoke(this, new(nameof(Background))); PropertyChanged?.Invoke(this, new(nameof(Foreground)));
  }
 }
}


