using GestionLibros.Data;
using GestionLibros.Views;
namespace GestionLibros;
public partial class App : Application
{
 private readonly AppDatabase database;
 public App(AppDatabase database) { InitializeComponent(); this.database = database; }
 protected override Window CreateWindow(IActivationState? activationState) => new(new LoginPage(database));
}
