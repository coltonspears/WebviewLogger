using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WebviewLogger
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

             AdvancedLogger.Initialize(19861);

            Random rand = new Random();

            while (true)
            {
                // Wait for a random delay between 1 and 2 seconds (1000 to 2000 milliseconds)
                int delay = rand.Next(1000, 2001);
                Thread.Sleep(delay);

                // Define example log message components.
                string message = "Example log message";
                string source = "RandomModule";
                string category = "Simulation";
                object data = new { Value = rand.Next(1000) };

                // Randomly choose one of the five logging methods.
                int choice = rand.Next(5);
                switch (choice)
                {
                    case 0:
                        AdvancedLogger.Info(message, source, category, data);
                        break;
                    case 1:
                        AdvancedLogger.Warning(message, source, category, data);
                        break;
                    case 2:
                        AdvancedLogger.Error(message, source, category, data);
                        break;
                    case 3:
                        AdvancedLogger.Debug(message, source, category, data);
                        break;
                    case 4:
                        AdvancedLogger.Critical(message, source, category, data);
                        break;
                }
            }
        }
    }
}
