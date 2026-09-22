using System.Drawing;
using System.Threading;
using System.Windows;
using OrbitOCR.Services;
using OrbitOCR.UI;

namespace OrbitOCR.Tests;

[TestClass]
public class UiSmokeTests
{
    /// <summary>
    /// Parsing a view resolves every StaticResource at load time, which the XAML compiler
    /// does not validate. Instantiating each view here catches missing theme keys and
    /// malformed control templates before they reach a running app.
    /// </summary>
    [TestMethod]
    public void Views_LoadWithoutXamlErrors()
    {
        Exception? error = null;

        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                {
                    var app = new Application();
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/OrbitOCR;component/UI/Theme.xaml", UriKind.Absolute)
                    });
                }

                var settings = new SettingsService();
                var ocr = new OcrService();

                // Parsing resolves every StaticResource; laying out instantiates the control
                // templates (ComboBox, switches, expander) that a parse alone would not build.
                var menu = new ActionMenu();
                menu.ShowForTextSelection("hello world", 2);
                menu.ShowForImageSelection(120, 80, "detected text");
                Measure(menu, 520, 140);
                Measure((FrameworkElement)new SettingsWindow(settings, ocr, _ => true, () => { }, () => { }).Content, 640, 720);

                using var bitmap = new Bitmap(200, 120);
                var bounds = new ScreenCaptureService().GetVirtualScreenBounds();
                var overlay = new OverlayWindow(bitmap, bounds, ocr, new SoundService(settings), settings, null);
                Measure((FrameworkElement)overlay.Content, 800, 600);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));

        Assert.IsNull(error, error?.ToString());
    }

    private static void Measure(FrameworkElement element, double width, double height)
    {
        element.Measure(new System.Windows.Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }
}
