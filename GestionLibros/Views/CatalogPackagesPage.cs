using GestionLibros.Data;
using GestionLibros.Data.Import;
using GestionLibros.Models;
using System.Text.Json;

namespace GestionLibros.Views;

// Master library stored in SQLite, independent of the original files after preload.
public sealed class CatalogPackagesPage : AccountingPage
{
    private readonly AppDatabase db;
    private readonly CollectionView packages = new() { SelectionMode = SelectionMode.Single, BackgroundColor = Gl2000Chrome.RowYellow };
    private readonly CollectionView accounts = new() { BackgroundColor = Gl2000Chrome.RowYellow };
    private readonly Label detail = ClassicWorkspaceChrome.Text("Seleccione un catálogo.", true);
    private List<Company> companies = [];
    private CatalogPackage? selected;

    public CatalogPackagesPage(AppDatabase db) : base("Catálogos precargados")
    {
        this.db = db;
        NavigationPage.SetHasNavigationBar(this, false);
        Title = "Administración · Catálogos precargados";
        packages.ItemTemplate = new DataTemplate(() =>
        {
            var label = ClassicWorkspaceChrome.Text("", true); label.Padding = new Thickness(6); label.SetBinding(Label.TextProperty, nameof(CatalogPackage.Name)); return label;
        });
        accounts.ItemTemplate = new DataTemplate(() =>
        {
            var grid = new Grid { ColumnDefinitions = { new(95), new(GridLength.Star), new(95) }, Padding = new Thickness(4, 1) };
            foreach (var (field, column) in new[] { (nameof(CatalogAccount.Code), 0), (nameof(CatalogAccount.Description), 1), (nameof(CatalogAccount.ParentCode), 2) })
            { var label = ClassicWorkspaceChrome.Text(""); label.SetBinding(Label.TextProperty, field); grid.Add(label, column, 0); }
            return grid;
        });
        packages.SelectionChanged += (_, e) =>
        {
            selected = e.CurrentSelection.FirstOrDefault() as CatalogPackage;
            var rows = selected == null ? [] : JsonSerializer.Deserialize<List<CatalogAccount>>(selected.Payload) ?? [];
            accounts.ItemsSource = rows;
            var assigned = companies.Where(c => c.CatalogPackageId == selected?.Id).Select(c => c.Name).ToList();
            detail.Text = selected == null ? "Seleccione un catálogo." : $"{rows.Count} cuentas · Sin saldos ni movimientos\n" + (assigned.Count == 0 ? "Disponible para asignar; se puede eliminar." : "Asignado a: " + string.Join(", ", assigned) + ". No se puede eliminar.");
        };
        var buttons = new HorizontalStackLayout { Spacing = 6, Children = {
            JournalsPage.ActionButton("Agregar catálogo", "gl_add.png", () => Guard(Add), 135),
            JournalsPage.ActionButton("Eliminar", "gl_delete.png", () => Guard(Delete), 90),
            JournalsPage.ActionButton("Volver", "gl_exit.png", async () => await Navigation.PopAsync(), 90) } };
        var preview = new Grid { RowDefinitions = { new(GridLength.Auto), new(24), new(GridLength.Star) } };
        preview.Add(detail, 0, 0);
        preview.Add(ClassicWorkspaceChrome.Text("Cuenta                       Descripción                                      Superior", true), 0, 1);
        preview.Add(accounts, 0, 2);
        var columns = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new(new GridLength(1, GridUnitType.Star)), new(new GridLength(2, GridUnitType.Star)) } };
        columns.Add(packages, 0, 0); columns.Add(preview, 1, 0);
        var body = new Grid { Padding = 12, RowSpacing = 8, BackgroundColor = Gl2000Chrome.HeaderBlue,
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        body.Add(ClassicWorkspaceChrome.Text("Catálogos guardados en el sistema · El maestro los asigna al crear la contabilidad", true), 0, 0);
        body.Add(buttons, 0, 1); body.Add(columns, 0, 2); body.Add(Notice, 0, 3); Content = body;
    }
    protected override async void OnAppearing() { base.OnAppearing(); await Guard(Load); }
    private async Task Load()
    {
        var id = selected?.Id;
        companies = await db.Companies();
        var list = await db.CatalogPackages(); packages.ItemsSource = list;
        packages.SelectedItem = list.FirstOrDefault(p => p.Id == id) ?? list.FirstOrDefault();
        if (list.Count == 0) { selected = null; accounts.ItemsSource = null; detail.Text = "No hay catálogos. Use Agregar catálogo."; }
    }
    private async Task Add()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Seleccione el catálogo GL2000 (.Tps)" });
        if (file == null) return;
        var rows = await Task.Run(() => GlCatalog.Read(file.FullPath));
        var name = await DisplayPromptAsync("Precargar catálogo", $"Se guardarán {rows.Count} cuentas sin saldos. Nombre del catálogo:", "Guardar", "Cancelar", initialValue: Path.GetFileNameWithoutExtension(file.FullPath));
        if (name == null) return;
        await db.PreloadCatalog(name, rows.Select(a => new CatalogAccount(a.Code, a.ParentCode, a.Description)).ToList());
        await Load(); Notice.Text = "Catálogo guardado dentro del sistema. Ya puede asignarlo al crear una contabilidad.";
    }
    private async Task Delete()
    {
        var package = selected ?? throw new InvalidOperationException("Seleccione el catálogo que desea eliminar.");
        if (companies.Any(c => c.CatalogPackageId == package.Id)) throw new InvalidOperationException("El catálogo está asignado. Se conserva para proteger las contabilidades existentes.");
        if (!await DisplayAlertAsync("Eliminar catálogo", $"¿Eliminar «{package.Name}» de los catálogos precargados?", "Eliminar", "Cancelar")) return;
        await db.DeleteCatalogPackage(package.Id); await Load(); Notice.Text = "Catálogo eliminado.";
    }
}
