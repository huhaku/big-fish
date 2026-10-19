using System.Windows;

namespace PetdexPet
{
    public partial class BubbleWindow : Window
    {
        public BubbleWindow()
        {
            InitializeComponent();
        }

        public void SetScale(double scale)
        {
            CloudBox.Width = 300 * scale;
            CloudBox.Height = 205 * scale;
        }
    }
}
