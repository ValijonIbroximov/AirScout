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
    
    public partial class MainWindow : Window
    {
        // =============================================
        // DOIMIY O'ZGARMASLAR
        // =============================================


        // Ma'lumotni yangilash oraligi (millisekundlarda)
        private const int DataUpdateIntervalMs = 500;

        // Xabarlarni ko'rsatish vaqti (soniyada)
        private const int NotificationDisplayTimeSec = 3;

        // Serial port ulanish tezligi
        private const int DefaultBaudRate = 9600;

        // Port tanlanmaganligi haqidagi xabar matni
        private const string NoPortSelectedText = "Portga ulanmagan yoki boshqa port tanlangan.";

        // Portlarni tekshirish oraligi (soniyada)
        private const int PortCheckIntervalSec = 1;


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
            //SidePanel.Height = ActualHeight - 100;
            UpdateAllUIElements();
        }

        /// <summary>
        /// Barcha UI elementlarni yangilash
        /// </summary>
        private void UpdateAllUIElements()
        {
            // Asosiy ko'rsatgichni yangilash

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
            double leftMargin = PositionX(grid, widthRatio);
            double topMargin = Math.Max(0, centerY - (grid.ActualHeight / 2));

            // Joylashuvni o'rnatish
            grid.Margin = new Thickness(leftMargin, topMargin, 0, 0);
            grid.HorizontalAlignment = HorizontalAlignment.Left;
            grid.VerticalAlignment = VerticalAlignment.Top;
        }

        private double PositionX(Grid grid, double widthRatio)
        {
            double centerX = ActualWidth * widthRatio;
            return Math.Max(0, centerX - (grid.ActualWidth / 2));
        }
        private double PositionY(Grid grid, double heightRatio)
        {
            double centerY = ActualHeight * heightRatio;
            return Math.Max(0, centerY - (grid.ActualHeight / 2));
        }

        /// <summary>
        /// Elementning tashqi ko'rinishini yangilash
        /// </summary>
        private void UpdateElementAppearance(Ellipse circle, Ellipse glowEffect, TextBlock percentText, double value)
        {
            
        }

        /// <summary>
        /// rangni hisoblash
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


        

        // Visual tree'dan elementlarni topish uchun yordamchi metod
        // Helper method to find visual children (put this in your MainWindow class)
        

    }
}