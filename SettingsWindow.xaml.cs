using System.Windows;
using System.Windows.Controls;

namespace PetdexPet
{
    public partial class SettingsWindow : Window
    {
        readonly MainWindow _owner;
        bool _init = true;

        public SettingsWindow(MainWindow owner, double initialScale)
        {
            InitializeComponent();
            _owner = owner;
            ScaleSlider.Value = initialScale;

            foreach (var name in owner.ActionNames)
            {
                var cb = new CheckBox
                {
                    Content = name,
                    IsChecked = owner.IsActionEnabled(name),
                    Margin = new Thickness(0, 3, 0, 3)
                };
                cb.Checked += (s, e) => owner.SetActionEnabled(name, true);
                cb.Unchecked += (s, e) => owner.SetActionEnabled(name, false);
                ActionList.Children.Add(cb);
            }
            BubblesBox.Text = string.Join(Environment.NewLine, owner.Bubbles);
            BubblesBox.TextChanged += (s, e) =>
            {
                var first = BubblesBox.Text.Split('\n').FirstOrDefault();
                _owner.PreviewBubbleText(first);
            };
            BubbleSizeSlider.Value = owner.BubbleScale;
            FontSlider.Value = owner.BubbleFont;
            DurSlider.Value = owner.BubbleSeconds;
            FreqSlider.Value = owner.BubbleFreq;
            ActionDurSlider.Value = owner.ActionSeconds;
            CountdownCheck.IsChecked = owner.OffWorkEnabled;
            OffWorkPinnedCheck.IsChecked = owner.OffWorkPinned;
            OffWorkTimeBox.Text = owner.OffWorkTimeText;
            CountdownTextBox.Text = owner.OffWorkText;
            _init = false;
            UpdateBubbleLabels();
        }

        void UpdateBubbleLabels()
        {
            BubbleSizeText.Text = $"气泡大小:{BubbleSizeSlider.Value:P0}";
            FontText.Text = $"文字大小:{FontSlider.Value:F0}";
            DurText.Text = $"气泡显示:{DurSlider.Value:F1} 秒";
            double f = FreqSlider.Value;
            FreqText.Text = f < 60 ? $"气泡频率:{f:F0} 秒"
                : f % 60 == 0 ? $"气泡频率:{f / 60:F0} 分"
                : $"气泡频率:{(int)(f / 60)} 分 {f % 60:F0} 秒";
            ActionDurText.Text = $"动作播放时长:{ActionDurSlider.Value:F1} 秒";
        }

        void ActionDur_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_init || _owner == null) return;
            ActionDurText.Text = $"动作播放时长:{ActionDurSlider.Value:F1} 秒";
            _owner.SetActionSeconds(ActionDurSlider.Value);
        }

        void BubblePref_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_init || _owner == null) return;
            UpdateBubbleLabels();
            _owner.SetBubblePrefs(BubbleSizeSlider.Value, FontSlider.Value, DurSlider.Value, FreqSlider.Value);
        }

        void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_owner == null) return;
            SizeText.Text = $"尺寸:{ScaleSlider.Value:P0}";
            _owner.ApplyScale(ScaleSlider.Value);
        }

        void Countdown_Changed(object sender, RoutedEventArgs e)
        {
            if (_init || _owner == null) return;
            _owner.SetOffWork(CountdownCheck.IsChecked == true, OffWorkTimeBox.Text, CountdownTextBox.Text);
        }

        void OffWorkPinned_Changed(object sender, RoutedEventArgs e)
        {
            if (_init || _owner == null) return;
            _owner.SetOffWorkPinned(OffWorkPinnedCheck.IsChecked == true);
        }

        void Save_Click(object sender, RoutedEventArgs e)
        {
            _owner.SetBubbles(BubblesBox.Text.Split('\n'));
            _owner.SetOffWork(CountdownCheck.IsChecked == true, OffWorkTimeBox.Text, CountdownTextBox.Text);
            _owner.SaveSettings();
            SaveBtn.Content = "已保存";
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            t.Tick += (s, e) => { t.Stop(); SaveBtn.Content = "保存"; };
            t.Start();
        }

        void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DragMove();
        }

        void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
