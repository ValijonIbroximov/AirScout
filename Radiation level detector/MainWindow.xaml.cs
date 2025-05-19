using System;
using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Radiation_level_detector
{
    public class SensorInfo
    {
        public string Name { get; set; }
        public string Coordinates { get; set; }
        public string District { get; set; }
        public double RadiationLevel { get; set; }
        public double DangerPercentage { get; set; }
    }
    public partial class MainWindow : Window
    {
        // =============================================
        // DOIMIY O'ZGARMASLAR
        // =============================================

        // Maksimal radiatsiya qiymati (nSv/h)
        private const double MaxRadiationValue = 10000.0;

        // Animatsiya davomiyligi (soniyada)
        private const double AnimationDuration = 1.5;

        // Ma'lumotni yangilash oraligi (millisekundlarda)
        private const int DataUpdateIntervalMs = 500;

        // Xabarlarni ko'rsatish vaqti (soniyada)
        private const int NotificationDisplayTimeSec = 3;

        // Serial port ulanish tezligi
        private const int DefaultBaudRate = 9600;

        // Port tanlanmaganligi haqidagi xabar matni
        private const string NoPortSelectedText = "Portga ulanmagan yoki boshqa port tanlangan.";

        // Portlarni tekshirish oraligi (soniyada)
        private const int PortCheckIntervalSec = 2;

        public double RadiationLevel = 0;
        public double RadiatioPercentage = 0;
        private bool btnClicked = false;

        // =============================================
        // UI ELEMENTLARNING JOYLASHUVI
        // =============================================

        // Asosiy va yordamchi ko'rsatgichlarning oynadagi nisbiy joylashuvi
        // (widthRatio, heightRatio) formatida
        private readonly (double width, double height)[] elementPositions = new[]
        {
            (0.55, 0.2),   // Asosiy ko'rsatgich
            (0.54, 0.02),  // 1-ko'rsatgich
            (0.46, 0.08),  // 2-ko'rsatgich
            (0.46, 0.73),   // 3-ko'rsatgich
            (0.523, 0.553)  // 4-ko'rsatgich
        };

        // =============================================
        // DASTUR HOLATINI SAQLASH UCHUN O'ZGARUVCHILAR
        // =============================================

        // Joriy radiatsiya qiymati (0-100% oralig'ida)
        private double currentRadiationValue = 0;

        // Serial port ulanishi uchun obyekt
        private SerialPort serialPort;

        // Xabarlarni vaqtincha ko'rsatish uchun taymer
        private DispatcherTimer notificationTimer;

        // UI ni yangilash uchun taymer
        private DispatcherTimer dataUpdateTimer;

        // Portlarni kuzatish uchun taymer
        private DispatcherTimer portCheckTimer;

        // Portlar ro'yxati yangilanayotganligini bildiradigan flag
        private bool isUpdatingPorts = false;

        // Oxirgi tanlangan port nomi
        private string lastSelectedPort = string.Empty;

        // Oldingi portlar ro'yxati (o'zgarishlarni aniqlash uchun)
        private string[] lastKnownPorts = Array.Empty<string>();



        // =============================================
        // ASOSIY OYNA KONSTRUKTORI
        // =============================================

        public MainWindow()
        {
            // WPF komponentlarini ishga tushirish
            InitializeComponent();

            // Taymerlarni sozlash
            InitializeTimers();

            // Port boshqaruvi tizimini ishga tushirish
            InitializePortManagement();

            // Barcha UI elementlarni yangilash
            UpdateAllUIElements();

            // Boshlang'ich holatda port tanlanmaganligini ko'rsatish
            currentValueText.Text = NoPortSelectedText;

            AttachMouseEventsToCircles();
        }

        // =============================================
        // DASTURNI ISHGA TUSHIRISH METODLARI
        // =============================================

        /// <summary>
        /// Barcha kerakli taymerlarni ishga tushirish
        /// </summary>
        private void InitializeTimers()
        {
            InitializeNotificationTimer();
            InitializeDataUpdateTimer();
        }

        /// <summary>
        /// Port boshqaruvi tizimini ishga tushirish
        /// </summary>
        private void InitializePortManagement()
        {
            // Oyna o'lchami o'zgarganda UI elementlarni qayta joylashtirish
            this.SizeChanged += OnWindowSizeChanged;

            // Portlar combobox'ini ishga tushirish
            InitializePortComboBox();

            // Port kuzatuvchisini ishga tushirish
            StartPortWatcher();
        }

        /// <summary>
        /// Xabarlarni ko'rsatish taymerini sozlash
        /// </summary>
        private void InitializeNotificationTimer()
        {
            notificationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(NotificationDisplayTimeSec)
            };
            notificationTimer.Tick += (s, e) =>
            {
                // Belgilangan vaqtdan keyin xabarni yashirish
                NotificationText.Visibility = Visibility.Collapsed;
                notificationTimer.Stop();
            };
        }

        /// <summary>
        /// UI yangilash taymerini ishga tushirish
        /// </summary>
        private void InitializeDataUpdateTimer()
        {
            dataUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DataUpdateIntervalMs)
            };
            dataUpdateTimer.Tick += (s, e) => UpdateAllUIElements();
            dataUpdateTimer.Start();
        }

        /// <summary>
        /// Portlar ro'yxatini boshlang'ich holatga keltirish
        /// </summary>
        private void InitializePortComboBox()
        {
            isUpdatingPorts = true;
            portComboBox.Items.Clear();
            portComboBox.Items.Add("Tanlang...");
            portComboBox.SelectedIndex = 0;
            RefreshPortList();
            isUpdatingPorts = false;
        }

        // =============================================
        // PORTLARNI BOSHQARISH METODLARI
        // =============================================

        /// <summary>
        /// Portlarni avtomatik kuzatish tizimini ishga tushirish
        /// </summary>
        private void StartPortWatcher()
        {
            try
            {
                // Faqat Windows tizimida ishlashini tekshirish
                if (!OperatingSystem.IsWindows())
                {
                    Debug.WriteLine("Port kuzatuvchisi faqat Windowsda ishlaydi");
                    return;
                }

                // Portlarni muntazam tekshirish uchun taymer
                portCheckTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(PortCheckIntervalSec)
                };

                // Boshlang'ich portlar ro'yxatini saqlash
                lastKnownPorts = SerialPort.GetPortNames();

                // Taymer ishga tushganda bajariladigan amallar
                portCheckTimer.Tick += (s, e) =>
                {
                    // Hozirgi mavjud portlarni olish
                    var currentPorts = SerialPort.GetPortNames();

                    // Yangi qo'shilgan portlarni aniqlash
                    var added = currentPorts.Except(lastKnownPorts);
                    foreach (var port in added)
                    {
                        // UI thread'ida xabar ko'rsatish va ro'yxatni yangilash
                        Dispatcher.Invoke(() => ShowNotification($"Yangi port qo'shildi: {port}"));
                        Dispatcher.Invoke(RefreshPortList);
                    }

                    // O'chirilgan portlarni aniqlash
                    var removed = lastKnownPorts.Except(currentPorts);
                    foreach (var port in removed)
                    {
                        // UI thread'ida xabar ko'rsatish va ro'yxatni yangilash
                        Dispatcher.Invoke(() => ShowNotification($"Port o'chirildi: {port}"));
                        Dispatcher.Invoke(RefreshPortList);
                    }

                    // Portlar ro'yxatini yangilash
                    lastKnownPorts = currentPorts;
                };

                // Taymerni ishga tushirish
                portCheckTimer.Start();
                Debug.WriteLine("Port kuzatuvchisi muvaffaqiyatli ishga tushirildi.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Port kuzatuvchisini ishga tushirishda xato: {ex.Message}");
            }
        }

        /// <summary>
        /// Portlar ro'yxatini yangilash
        /// </summary>
        private void RefreshPortList()
        {
            // Faqat Windows tizimida ishlashini tekshirish
            if (!OperatingSystem.IsWindows())
            {
                ShowNotification("Serial portlar faqat Windowsda ishlaydi");
                return;
            }

            try
            {
                isUpdatingPorts = true;

                // Mavjud portlarni olish
                string[] ports = SerialPort.GetPortNames();

                // Oldingi tanlovni saqlab qolish
                string currentSelection = portComboBox.SelectedItem?.ToString();

                // Ro'yxatni tozalash va boshlang'ich elementni qo'shish
                portComboBox.Items.Clear();
                portComboBox.Items.Add("Tanlang...");

                // Mavjud portlarni ro'yxatga qo'shish
                foreach (string port in ports)
                {
                    portComboBox.Items.Add(port);
                }

                // Agar oldin tanlangan port hali mavjud bo'lsa, uni tanlab qo'yish
                if (!string.IsNullOrEmpty(lastSelectedPort) && ports.Contains(lastSelectedPort))
                {
                    portComboBox.SelectedItem = lastSelectedPort;
                }
                // Yoki avvalgi tanlov hali mavjud bo'lsa
                else if (!string.IsNullOrEmpty(currentSelection) && ports.Contains(currentSelection))
                {
                    portComboBox.SelectedItem = currentSelection;
                }
                // Aks holda boshlang'ich holatga o'tkazish
                else
                {
                    portComboBox.SelectedIndex = 0;
                    currentValueText.Text = NoPortSelectedText;
                }

                Debug.WriteLine($"Portlar ro'yxati yangilandi. Jami {ports.Length} ta port.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Portlarni yangilashda xato: {ex.Message}");
                ShowNotification($"Portlarni yangilashda xato: {ex.Message}");
            }
            finally
            {
                isUpdatingPorts = false;
            }
        }

        /// <summary>
        /// Port tanlanganda yoki o'zgartirilganda ishlaydigan metod
        /// </summary>
        private void portComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Agar portlar yangilanayotgan bo'lsa, hech narsa qilmaslik
            if (isUpdatingPorts) return;

            // Agar "Tanlang..." tanlangan bo'lsa
            if (portComboBox.SelectedIndex == 0)
            {
                // Agar oldin port tanlangan bo'lsa, xabar ko'rsatish
                if (!string.IsNullOrEmpty(lastSelectedPort))
                {
                    ShowNotification($"{lastSelectedPort} portidan chiqildi");
                    lastSelectedPort = string.Empty;
                }

                // Portni yopish va xabar ko'rsatish
                ClosePort();
                currentValueText.Text = NoPortSelectedText;
                return;
            }

            // Yangi port tanlangan bo'lsa
            if (portComboBox.SelectedItem != null)
            {
                lastSelectedPort = portComboBox.SelectedItem.ToString();
                OpenPort(lastSelectedPort);
            }
        }

        /// <summary>
        /// Belgilangan portga ulanish
        /// </summary>
        /// <param name="portName">Ulanish uchun port nomi</param>
        private void OpenPort(string portName)
        {
            // Avvalgi ulanishni yopish
            ClosePort();

            try
            {
                // Yangi serial port obyektini yaratish
                serialPort = new SerialPort(portName, DefaultBaudRate);

                // Ma'lumot kelganda ishlaydigan metodni ulash
                serialPort.DataReceived += SerialPort_DataReceived;

                // Xato yuz berganda ishlaydigan metod
                serialPort.ErrorReceived += (s, e) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        ShowNotification($"{portName} portida xato yuz berdi");
                        currentValueText.Text = NoPortSelectedText;
                    });
                };

                // Portni ochish
                serialPort.Open();
                ShowNotification($"{portName} porti muvaffaqiyatli ulandi!");
            }
            catch (Exception ex)
            {
                // Xato yuz berganda xabar ko'rsatish va boshlang'ich holatga qaytarish
                ShowNotification($"Portni ochishda xatolik: {ex.Message}");
                portComboBox.SelectedIndex = 0;
                currentValueText.Text = NoPortSelectedText;
            }
        }

        /// <summary>
        /// Portdan ma'lumot kelganda ishlaydigan metod
        /// </summary>
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (serialPort == null || !serialPort.IsOpen) return;

                string data = serialPort.ReadLine().Trim();

                if (double.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out double radiationValue))
                {
                    Dispatcher.Invoke(() =>
                    {
                        // 2 ta kasr bilan hisoblash
                        double percentage = Math.Round((radiationValue / MaxRadiationValue) * 100.0, 2);
                        currentValueText.Text = $"{radiationValue:F2} nSv/h";
                        RadiationLevel = radiationValue;
                        currentRadiationValue = percentage; // Butun songa yaxlitlamasdan saqlash
                        RadiatioPercentage = currentRadiationValue;
                        // UI ni yangilash
                        UpdateAllUIElements();
                    });
                }
                else
                {
                    Dispatcher.Invoke(() => {
                        ShowNotification($"Noto'g'ri ma'lumot formati: '{data}'");
                        currentValueText.Text = NoPortSelectedText;
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ShowNotification($"Ma'lumot o'qishda xato: {ex.Message}");
                    currentValueText.Text = NoPortSelectedText;
                });
            }
        }

        /// <summary>
        /// Portni yopish va resurslarni tozalash
        /// </summary>
        private void ClosePort()
        {
            if (serialPort != null)
            {
                try
                {
                    // Agar port ochiq bo'lsa
                    if (serialPort.IsOpen)
                    {
                        // Hodisalarni olib tashlash va portni yopish
                        serialPort.DataReceived -= SerialPort_DataReceived;
                        serialPort.Close();
                    }

                    // Resurslarni tozalash
                    serialPort.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Portni yopishda xato: {ex.Message}");
                }
                finally
                {
                    serialPort = null;
                }
            }
        }

        // =============================================
        // UI BOSHQARISH METODLARI
        // =============================================

        /// <summary>
        /// Foydalanuvchiga xabar ko'rsatish
        /// </summary>
        /// <param name="message">Ko'rsatiladigan xabar matni</param>
        private void ShowNotification(string message)
        {
            Panel.SetZIndex(NotificationText, 1001);
            NotificationText.Text = message;
            NotificationText.Visibility = Visibility.Visible;
            notificationTimer.Start();
        }

        /// <summary>
        /// Oyna o'lchami o'zgarganda UI elementlarni qayta joylashtirish
        /// </summary>
        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            SidePanel.Height = ActualHeight - 100;
            UpdateAllUIElements();
        }

        /// <summary>
        /// Barcha UI elementlarni yangilash
        /// </summary>
        private void UpdateAllUIElements()
        {
            // Asosiy ko'rsatgichni yangilash
            UpdateUIElement(AnimationGrid, elementPositions[0].width, elementPositions[0].height,
                          Circle, GlowEffect, glowScale, PercentText, currentRadiationValue);

            // Yordamchi ko'rsatgichlarni yangilash (qiymat 0)
            for (int i = 1; i < elementPositions.Length; i++)
            {
                UpdateUIElement(
                    i == 1 ? AnimationGrid1 : i == 2 ? AnimationGrid2 : i == 3 ? AnimationGrid3 : AnimationGrid4,
                    elementPositions[i].width, elementPositions[i].height,
                    i == 1 ? Circle1 : i == 2 ? Circle2 : i == 3 ? Circle3 : Circle4,
                    i == 1 ? GlowEffect1 : i == 2 ? GlowEffect2 : i == 3 ? GlowEffect3 : GlowEffect4,
                    i == 1 ? glowScale1 : i == 2 ? glowScale2 : i == 3 ? glowScale3 : glowScale4,
                    i == 1 ? PercentText1 : i == 2 ? PercentText2 : i == 3 ? PercentText3 : PercentText4,
                    0);
            }
        }

        /// <summary>
        /// Yakka UI elementni yangilash
        /// </summary>
        private void UpdateUIElement(Grid grid, double widthRatio, double heightRatio,
                Ellipse circle, Ellipse glowEffect, ScaleTransform glowScale,
                TextBlock percentText, double value)
        {
            // Element joylashuvini yangilash
            UpdateElementPosition(grid, widthRatio, heightRatio);

            // Ma'lumot kelmayotgan holatni tekshirish
            bool noData = currentValueText.Text == NoPortSelectedText;

            // Ko'rinishini yangilash
            if (grid == AnimationGrid) 
                UpdateElementAppearance(circle, glowEffect, percentText, value,  noData);

            // Animatsiyani boshlash (agar kerak bo'lsa)
            StartGlowAnimationIfNeeded(glowEffect, glowScale, false); //(grid == AnimationGrid) ? noData : false
        }
        /// <summary>
        /// Elementning tashqi ko'rinishini yangilash
        /// </summary>
        private void UpdateElementAppearance(Ellipse circle, Ellipse glowEffect, TextBlock percentText, double value, bool noData)
        {
            if (noData)
            {
                // Ma'lumot kelmayotgan holat
                Color grayColor = Color.FromRgb(100, 100, 100); // Kulrang rang
                circle.Fill = new SolidColorBrush(grayColor);
                glowEffect.Fill = new SolidColorBrush(grayColor);
                percentText.Text = "?";
                percentText.Foreground = Brushes.White; // Qoraroq matn rangi DarkGray


                InfoRadiation.Text = $"Radiatsiya darajasi: ? nSv/h";
                InfoDanger.Text = $"Insonga xavfi darajasi: ? %";
                label0.Content = $"? %";
                label0.Background = new SolidColorBrush(grayColor);
                label01.Content = $"? %";
                label01.Background = new SolidColorBrush(grayColor);
            }
            else
            {
                //ShowNotification($"{glowEffect.Name} ");
                // Normal holat
                Color color = CalculateRadiationColor(value);
                circle.Fill = new SolidColorBrush(color);
                glowEffect.Fill = new SolidColorBrush(color);
                percentText.Text = $"{value:F2}%";
                percentText.Foreground = Brushes.Blue; // Oq matn rangi

                InfoRadiation.Text = $"Radiatsiya darajasi: {RadiationLevel:F2} nSv/h";
                InfoDanger.Text = $"Insonga xavfi darajasi: {value:F2}%";
                label0.Content = $"{value:F2}%";
                label0.Background = new SolidColorBrush(color);
                label01.Content = $"{value:F2}%";
                label01.Background = new SolidColorBrush(color);
            }
        }

        /// <summary>
        /// Yorug'lik animatsiyasini boshlash (agar hali boshlanmagan bo'lsa)
        /// </summary>
        private void StartGlowAnimationIfNeeded(Ellipse glowEffect, ScaleTransform glowScale, bool noData)
        {
            // Agar ma'lumot kelmayotgan bo'lsa, animatsiyani o'chirish
            if (noData)
            {
                glowEffect.BeginAnimation(UIElement.OpacityProperty, null);
                glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                glowEffect.Opacity = 0.3; // Past shaffoflik
                return;
            }

            // Agar animatsiya allaqachon ishlamayotgan bo'lsa
            if (!glowScale.HasAnimatedProperties)
            {
                // Kattalik animatsiyasi
                var glowAnimation = new DoubleAnimation
                {
                    From = 0.4,
                    To = 1.5,
                    Duration = TimeSpan.FromSeconds(AnimationDuration),
                    RepeatBehavior = RepeatBehavior.Forever
                };

                // Shaffoflik animatsiyasi
                var opacityAnimation = new DoubleAnimation
                {
                    From = 0.5,
                    To = 0.0,
                    Duration = TimeSpan.FromSeconds(AnimationDuration),
                    RepeatBehavior = RepeatBehavior.Forever
                };

                // Animatsiyalarni boshlash
                glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, glowAnimation);
                glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, glowAnimation);
                glowEffect.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            }
        }

        /// <summary>
        /// Elementning oynadagi joylashuvini yangilash
        /// </summary>
        private void UpdateElementPosition(Grid grid, double widthRatio, double heightRatio)
        {
            // Markaz nuqtasini hisoblash
            double centerX = ActualWidth * widthRatio;
            double centerY = ActualHeight * heightRatio;

            // Margin qiymatlarini hisoblash (manfiy bo'lmasligiga ishonch hosil qilish)
            double leftMargin = CirclePositionX(grid, widthRatio);
            double topMargin = Math.Max(0, centerY - (grid.ActualHeight / 2));

            // Joylashuvni o'rnatish
            grid.Margin = new Thickness(leftMargin, topMargin, 0, 0);
            grid.HorizontalAlignment = HorizontalAlignment.Left;
            grid.VerticalAlignment = VerticalAlignment.Top;
        }

        private double CirclePositionX(Grid grid, double widthRatio)
        {
            double centerX = ActualWidth * widthRatio;
            return Math.Max(0, centerX - (grid.ActualWidth / 2));
        }
        private double CirclePositionY(Grid grid, double heightRatio)
        {
            double centerY = ActualHeight * heightRatio;
            return Math.Max(0, centerY - (grid.ActualHeight / 2));
        }

        /// <summary>
        /// Elementning tashqi ko'rinishini yangilash
        /// </summary>
        private void UpdateElementAppearance(Ellipse circle, Ellipse glowEffect, TextBlock percentText, double value)
        {
            // Radiatsiya darajasiga qarab rangni hisoblash
            Color color = CalculateRadiationColor(value);

            // Elementlarga rang berish
            circle.Fill = new SolidColorBrush(color);
            glowEffect.Fill = new SolidColorBrush(color);

            // Foiz qiymatini ko'rsatish
            percentText.Text = $"{value}%";
        }

        /// <summary>
        /// Radiatsiya darajasiga qarab rangni hisoblash
        /// </summary>
        /// <returns>
        /// Qizil (yuqori daraja) -> Sariq (o'rta) -> Yashil (past daraja)
        /// </returns>
        private Color CalculateRadiationColor(double value)
        {
            value = (value >=0 && value <= 100) ? value : 100;
            // Qizil komponent (yuqori darajada maksimal)
            byte r = (byte)((value > 50) ? 255 : 255 * value / 50);

            // Yashil komponent (past darajada maksimal)
            byte g = (byte)((value <= 50) ? 255 : 255 * (100 - value) / 50);

            // Ko'k komponent ishlatilmaydi
            return Color.FromRgb(r, g, 0);
        }

        /// <summary>
        /// Yorug'lik animatsiyasini boshlash (agar hali boshlanmagan bo'lsa)
        /// </summary>
        private void StartGlowAnimationIfNeeded(Ellipse glowEffect, ScaleTransform glowScale)
        {
            // Agar animatsiya allaqachon ishlamayotgan bo'lsa
            if (!glowScale.HasAnimatedProperties)
            {
                // Kattalik animatsiyasi
                var glowAnimation = new DoubleAnimation
                {
                    From = 0.4,
                    To = 1.5,
                    Duration = TimeSpan.FromSeconds(AnimationDuration),
                    RepeatBehavior = RepeatBehavior.Forever
                };

                // Shaffoflik animatsiyasi
                var opacityAnimation = new DoubleAnimation
                {
                    From = 0.5,
                    To = 0.0,
                    Duration = TimeSpan.FromSeconds(AnimationDuration),
                    RepeatBehavior = RepeatBehavior.Forever
                };

                // Animatsiyalarni boshlash
                glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, glowAnimation);
                glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, glowAnimation);
                glowEffect.BeginAnimation(UIElement.OpacityProperty, opacityAnimation);
            }
        }

        // =============================================
        // RESURSLARNI TOZALASH
        // =============================================

        /// <summary>
        /// Oyna yopilganda resurslarni tozalash
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            CleanupResources();
        }

        /// <summary>
        /// Barcha resurslarni tozalash
        /// </summary>
        private void CleanupResources()
        {
            // Portni yopish
            ClosePort();

            // Ma'lumot yangilash taymerini to'xtatish
            if (dataUpdateTimer != null)
            {
                dataUpdateTimer.Stop();
                dataUpdateTimer = null;
            }

            // Port kuzatish taymerini to'xtatish
            if (portCheckTimer != null)
            {
                portCheckTimer.Stop();
                portCheckTimer = null;
            }
        }


        // =============================================
        // Popup ma'lumotlar paneli
        // =============================================

        private void AttachMouseEventsToCircles()
        {
            // Asosiy Circle
            Circle.MouseEnter += Circle_MouseEnter;
            Circle.MouseLeave += Circle_MouseLeave;
            PercentText.MouseEnter += Circle_MouseEnter;
            PercentText.MouseLeave += Circle_MouseLeave;

            // Boshqa Circle elementlari
            Circle1.MouseEnter += Circle_MouseEnter;
            Circle1.MouseLeave += Circle_MouseLeave;
            PercentText1.MouseEnter += Circle_MouseEnter;
            PercentText1.MouseLeave += Circle_MouseLeave;

            Circle2.MouseEnter += Circle_MouseEnter;
            Circle2.MouseLeave += Circle_MouseLeave;
            PercentText2.MouseEnter += Circle_MouseEnter;
            PercentText2.MouseLeave += Circle_MouseLeave;

            Circle3.MouseEnter += Circle_MouseEnter;
            Circle3.MouseLeave += Circle_MouseLeave;
            PercentText3.MouseEnter += Circle_MouseEnter;
            PercentText3.MouseLeave += Circle_MouseLeave;

            Circle4.MouseEnter += Circle_MouseEnter;
            Circle4.MouseLeave += Circle_MouseLeave;
            PercentText4.MouseEnter += Circle_MouseEnter;
            PercentText4.MouseLeave += Circle_MouseLeave;
        }

        // Yoki LINQ yordamida avtomatik ravishda topib qo'shish
        private void AttachMouseEventsAutomatically()
        {
            // Now we can call it without parameters
            var circles = FindVisualChildren<Ellipse>()
                .Where(e => e.Name?.StartsWith("Circle") == true);

            foreach (var circle in circles)
            {
                circle.MouseEnter += Circle_MouseEnter;
                circle.MouseLeave += Circle_MouseLeave;
            }
        }

        // Visual tree'dan elementlarni topish uchun yordamchi metod
        // Helper method to find visual children (put this in your MainWindow class)
        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj = null) where T : DependencyObject
        {
            if (depObj == null)
            {
                // If no parameter provided, use the current window as starting point
                depObj = Application.Current.MainWindow;
            }

            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T t)
                    {
                        yield return t;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }

        // MouseEnter event handleri
        private void Circle_MouseEnter(object sender, MouseEventArgs e)
        {
            try
            {
                string elementName = string.Empty;
                FrameworkElement element = null;

                // Determine if sender is Ellipse or TextBlock
                if (sender is Ellipse ellipse)
                {
                    elementName = ellipse.Name;
                    element = ellipse;
                }
                else if (sender is TextBlock textBlock)
                {
                    elementName = textBlock.Name;
                    element = textBlock;
                }

                if (element != null)
                {
                    // Sensor ma'lumotlarini aniqlash
                    var sensorInfo = GetSensorInfo(elementName);

                    // Info panelni to'ldirish
                    InfoName.Text = $"Nom: {sensorInfo.Name}";
                    InfoRadiation.Text = $"Radiatsiya darajasi: {sensorInfo.RadiationLevel:F2} nSv/h";
                    InfoDanger.Text = $"Insonga xavfi darajasi: {sensorInfo.DangerPercentage:F2}%";
                    InfoCoordinates.Text = $"Koordinatasi: {sensorInfo.Coordinates}";
                    InfoDistrict.Text = $"Okrug: {sensorInfo.District}";

                    // Panelni pozitsiyalashtirish va ustunlik berish
                    Point mousePos = e.GetPosition(this);

                    if (btnClicked)
                    {
                        InfoPanel.Margin = Circle.Margin;
                        btnClicked = false;
                    }
                    else InfoPanel.Margin = new Thickness(mousePos.X + 10, mousePos.Y + 10, 0, 0);
                    InfoPanel.Visibility = Visibility.Visible;

                    // Xato haqida foydalanuvchiga xabar berish
                    //ShowNotification(string.Empty);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MouseEnter xatosi: {ex.Message}");
                // Xatolik yuz berganda panelni yashirish va xatoni ko'rsatish
                InfoPanel.Visibility = Visibility.Collapsed;
                ShowNotification($"Xatolik: {ex.Message}");
            }
        }

        private void Circle_MouseLeave(object sender, MouseEventArgs e)
        {
            InfoPanel.Visibility = Visibility.Collapsed;
        }

        // Sensor ma'lumotlarini olish uchun metod
        private SensorInfo GetSensorInfo(string circleName)
        {
            // Sensor ma'lumotlaringizni qaytaradigan kod
            // Misol uchun:
            switch (circleName)
            {
                case "Circle": 
                case "PercentText":
                    return new SensorInfo
                    {
                        Name = "AKTAHI",
                        RadiationLevel = this.RadiationLevel,
                        DangerPercentage = this.RadiatioPercentage,
                        Coordinates = "41.207783, 69.137821",
                        District = "THO"
                    };
                case "Circle1":
                case "PercentText1":
                    return new SensorInfo
                    {
                        Name = "71186 h/q",
                        RadiationLevel = 0,
                        DangerPercentage = 0,
                        Coordinates = "41.210269, 69.137290",
                        District = "THO"
                    };
                case "Circle2":
                case "PercentText2":
                    return new SensorInfo
                    {
                        Name = "Markaziy aloqa uzeli",
                        RadiationLevel = 0,
                        DangerPercentage = 0,
                        Coordinates = "41.20975, 69.13435",
                        District = "THO"
                    };
                case "Circle3":
                case "PercentText3":
                    return new SensorInfo
                    {
                        Name = "29262 h/q",
                        RadiationLevel = 0,
                        DangerPercentage = 0,
                        Coordinates = "41.199549, 69.134334",
                        District = "THO"
                    };
                case "Circle4":
                case "PercentText4":
                    return new SensorInfo
                    {
                        Name = "Maxsus avariya tiklash boshqarmasi",
                        RadiationLevel = 0,
                        DangerPercentage = 0,
                        Coordinates = "41.202253, 69.136931",
                        District = "THO"
                    };
                // ... boshqa sensorlar
                default:
                    return new SensorInfo
                    {
                        Name = "Noma'lum",
                        RadiationLevel = 0,
                        DangerPercentage = 0,
                        Coordinates = "0, 0",
                        District = "Noma'lum"
                    };
            }
        }


        ////////////////////////////////////////
        /// Buttonlar uchun
        ////////////////////////////////////////
        ///

        private bool _isMenuOpen = false;

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            _isMenuOpen = !_isMenuOpen;
            int start = 220, end = 0;
            var animation = new DoubleAnimation
            {
                To = _isMenuOpen ? start : end,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            SidePanelTransform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        private bool _isFiltr = false;

        private void FiltrButton_Click(object sender, RoutedEventArgs e)
        {
            _isFiltr = !_isFiltr;
            if (_isFiltr)
            {
                ((Button)sender).Content = "Filtr: Daraja";
                filtrDaraja.Visibility = Visibility.Visible;
                filtrOkrug.Visibility = Visibility.Collapsed;
            }
            else
            {
                ((Button)sender).Content = "Filtr: Okruglar";
                filtrDaraja.Visibility = Visibility.Collapsed;
                filtrOkrug.Visibility = Visibility.Visible;
            }
        }

        private bool _isTHO = false;

        private void thoButton_Click(object sender, RoutedEventArgs e)
        {
            _isTHO = !_isTHO;
            if (_isTHO)
            {
                THOmenu.Visibility = Visibility.Visible;
            }
            else
            {
                THOmenu.Visibility = Visibility.Collapsed;
            }
        }


        private void Button_Click(object sender, RoutedEventArgs e)
        {
            ShowInfo(Circle);
        }
        private void Button1_Click(object sender, RoutedEventArgs e)
        {
            ShowInfo(Circle1);
        }
        private void Button2_Click(object sender, RoutedEventArgs e)
        {
            ShowInfo(Circle2);
        }
        private void Button3_Click(object sender, RoutedEventArgs e)
        {
            ShowInfo(Circle3);
        }
        private void Button4_Click(object sender, RoutedEventArgs e)
        {
            ShowInfo(Circle4);
        }
        private void Button5_Click(object sender, RoutedEventArgs e)
        {
            ShowNotification("Harbiy qismlar topilmadi!");
        }

        public void ShowInfo(FrameworkElement targetElement)
        {
            try
            {
                // Get the element name (Circle1, Circle2, etc.)
                string elementName = targetElement.Name;

                // Get sensor info
                var sensorInfo = GetSensorInfo(elementName);

                // Fill the info panel
                InfoName.Text = $"Nom: {sensorInfo.Name}";
                InfoRadiation.Text = $"Radiatsiya darajasi: {sensorInfo.RadiationLevel:F2} nSv/h";
                InfoDanger.Text = $"Insonga xavfi darajasi: {sensorInfo.DangerPercentage:F2}%";
                InfoCoordinates.Text = $"Koordinatasi: {sensorInfo.Coordinates}";
                InfoDistrict.Text = $"Okrug: {sensorInfo.District}";

                // Calculate position
                var elementTransform = targetElement.TransformToVisual(this);
                var elementPosition = elementTransform.Transform(new Point(0, 0));

                // Position the panel to the right of the element
                double left = elementPosition.X + 30;
                double top = elementPosition.Y + 40;

                // Apply position with animation
                InfoPanel.Margin = new Thickness(left, top, 0, 0);

                // Show with fade animation
                InfoPanel.Visibility = Visibility.Visible;
                InfoPanel.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShowInfo error: {ex.Message}");
                ShowNotification($"Xatolik: {ex.Message}");
            }
        }
    }
}