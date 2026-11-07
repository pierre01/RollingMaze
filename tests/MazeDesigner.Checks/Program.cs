using MazeDesigner;
using RollingMaze.Mazes;
using System.Windows;
using System.Windows.Controls;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Load the actual compiled XAML: a successful build alone does not validate
        // the runtime types returned by markup extensions such as x:Static.
        var application = new Application();
        var window = new MainWindow();
        var board = (Canvas)window.FindName("Board");
        var root = (FrameworkElement)window.Content;
        foreach (var size in new[] { new Size(900, 1000), new Size(1400, 700) })
        {
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
            if (board.ActualWidth != MazeLayout.Width || board.ActualHeight != MazeLayout.Height)
                throw new Exception($"Unexpected logical board size: {board.RenderSize}.");
            var bounds = board.TransformToAncestor(root).TransformBounds(new Rect(board.RenderSize));
            if (Math.Abs(bounds.Width / bounds.Height - 1.25) > 0.000001)
                throw new Exception($"Unexpected displayed board aspect ratio: {bounds}.");
        }
        window.Close();
        application.Shutdown();
        Console.WriteLine("Designer XAML loading and portrait/landscape layout checks passed.");
    }
}
