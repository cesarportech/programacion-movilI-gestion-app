using System.Globalization;
using GestionLibros.Data;
using GestionLibros.Models;
namespace GestionLibros.Views;
internal static class AccountingUi
{
 internal static readonly Color Yellow = Gl2000Chrome.RowYellow;
 internal static readonly Color Blue = Gl2000Chrome.HeaderBlue;
 internal static readonly Color Green = Color.FromArgb("#99F0CC");
 internal static string Money(long cents) {
  var value = cents / 100m;
  var text = Math.Abs(value).ToString("N2", CultureInfo.CurrentCulture);
  return value < 0 ? $"({text})" : text;
 }
 internal static Label Label(string text, bool bold = false) => new() { Text = text, TextColor = Colors.Black, FontSize = 14, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None, VerticalOptions = LayoutOptions.Center };
 internal static Button Button(string text, Func<Task> action) {
  var button = new Button { Text = text, BackgroundColor = Blue, TextColor = Colors.Black, CornerRadius = 2, FontSize = 14, Padding = new Thickness(12, 6) };
  button.Clicked += async (_, _) => { button.IsEnabled = false; try { await action(); } finally { button.IsEnabled = true; } }; return button;
 }
 // linkColumns colors the first N columns like the blue account/description text in the original tables;
 // a value already wrapped in parentheses (negative amounts) always renders in red, like the old screens.
 internal static View Row(string[] values, bool header = false, Action? select = null, bool selected = false, int linkColumns = 0) {
  var background = selected ? Gl2000Chrome.SelectedNavy : header ? Blue : Yellow;
  var grid = new Grid { ColumnSpacing = 1, Padding = 4, BackgroundColor = background };
  for (var i = 0; i < values.Length; i++) {
   grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 1 ? new GridLength(2, GridUnitType.Star) : GridLength.Star });
   var negative = values[i].StartsWith('(') && values[i].EndsWith(')');
   var label = Label(values[i], header);
   label.TextColor = selected ? Colors.White : header ? Colors.Black : negative ? Gl2000Chrome.NegativeRed : i < linkColumns ? Gl2000Chrome.LinkBlue : Colors.Black;
   label.Padding = new Thickness(4);
   grid.Add(label, i, 0);
  }
  if (select != null) { var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => select(); grid.GestureRecognizers.Add(tap); }
  return grid;
 }
 internal static ScrollView Table(VerticalStackLayout rows) => new() { Content = rows, Orientation = ScrollOrientation.Both };
}
public class AccountingPage : ContentPage
{
 protected readonly VerticalStackLayout Body = new() { Spacing = 10, Padding = 16 };
 protected readonly Label Notice = new() { TextColor = Colors.DarkRed };

 // Chrome-less constructor: classic pages call it and then replace Content with their own frame.
 protected AccountingPage(string title, string? texture = null)
 {
  Title = title; BackgroundColor = Color.FromArgb("#E9E9DF");
  Body.Children.Add(AccountingUi.Label(title, true));
  if (texture == null) { Content = new ScrollView { Content = Body }; return; }
  var (file, w, h) = Gl2000Chrome.Texture(texture);
  Content = Gl2000Chrome.Backdrop(new ScrollView { Content = Body }, file, w, h);
 }

 // Full chrome: menu bar + icon toolbar (when a fixed company/year/month is known) + status bar, matching
 // the persistent frame every GL2000 screenshot shows around its content. "texture" picks which estilo.ini
 // wallpaper shows behind the content (see Gl2000Chrome.Texture) — "form" for data-entry screens, "card"
 // (default) for browse/report/utility screens, matching the Process/Report category in the original.
 protected AccountingPage(string title, AppDatabase db, Company? company, int? year, int? month, string hint = "", Func<Task>? onExit = null, string texture = "card")
 {
  Title = company != null ? $"FREDI Contabilidad - {company.Name} - [{title}]" : $"FREDI Contabilidad - [{title}]";
  BackgroundColor = Color.FromArgb("#E9E9DF");
  Body.Children.Add(AccountingUi.Label(title, true));

  var layout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
  if (company != null && year != null && month != null)
  {
   Gl2000Chrome.ApplyMenus(this, key => Gl2000Chrome.Navigate(this, db, company, year.Value, month.Value, key));
   layout.Add(Gl2000Chrome.Toolbar(this, db, company, year.Value, month.Value, onExit), 0, 0);
  }
  else Gl2000Chrome.ApplyMenus(this, null);
  var (file, w, h) = Gl2000Chrome.Texture(texture);
  layout.Add(Gl2000Chrome.Backdrop(new ScrollView { Content = Body }, file, w, h), 0, 1);
  layout.Add(Gl2000Chrome.StatusBar(db, hint), 0, 2);
  Content = layout;
 }

 // Runs one operation at a time. A flag instead of Content.IsEnabled: disabling the page cascaded
 // through every control on each query, which made pages slow to respond.
 private bool working;
 protected async Task Guard(Func<Task> work) { if (working) return; working = true; Notice.Text = ""; try { await work(); } catch (InvalidOperationException ex) { Notice.Text = ex.Message; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); Notice.Text = "No se pudo completar la operación. Revise los datos e inténtelo nuevamente."; } finally { working = false; } }
}
