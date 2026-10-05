namespace GestionLibros.Views;

// Matches [Basic3 About] WallPaper=BGMarble.bmp from estilo.ini — the one window category in the
// original style sheet that had no equivalent screen yet, so it gets its own small page here.
public class AboutPage : ContentPage
{
    public AboutPage()
    {
        Title = "Acerca de";
        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#F2F2F2"),
            Stroke = Colors.Gray,
            StrokeThickness = 1,
            Padding = 24,
            WidthRequest = 360,
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Label { Text = "FREDI", FontSize = 32, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center },
                    new Label { Text = "Sistema de Contabilidad", FontSize = 16, HorizontalTextAlignment = TextAlignment.Center },
                    new Label { Text = "Versión de pruebas. Conserva la estructura, los procesos y la apariencia del sistema GL 2000.", FontSize = 13, HorizontalTextAlignment = TextAlignment.Center },
                    AccountingUi.Button("Cerrar", async () => await Navigation.PopAsync()),
                },
            },
        };
        var centered = new Grid { Children = { card } };
        var (file, w, h) = Gl2000Chrome.Texture("about");
        Content = Gl2000Chrome.Backdrop(new ScrollView { Content = centered }, file, w, h);
    }
}
