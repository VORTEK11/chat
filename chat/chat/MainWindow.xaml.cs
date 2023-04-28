using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace chat
{
    public partial class MainWindow : Window
    {
        // Объект для работы с TcpClient
        private TcpClient _client;

        // Объект для работы с TcpListener
        private TcpListener _listener;

        // Объект для отмены прослушивания в случае отключения пользователя
        private CancellationTokenSource _cancellationTokenSource;

        // Список подключенных пользователей
        private readonly List<User> _users = new List<User>();

        // Класс, представляющий пользователя
        private class User
        {
            public string Name { get; set; }
            public TcpClient Client { get; set; }
            public DateTime ConnectedAt { get; set; }
        }

        public MainWindow()
        {
            InitializeComponent();
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            // Проверка ввода имени пользователя
            if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
            {
                MessageBox.Show("Введите имя пользователя");
                return;
            }

            // Получение IP-адреса и порта из TextBox
            if (!IPAddress.TryParse(IpAddressTextBox.Text, out var ipAddress))
            {
                MessageBox.Show("Введите корректный IP-адрес");
                return;
            }

            if (!int.TryParse(PortTextBox.Text, out var port))
            {
                MessageBox.Show("Введите корректный порт");
                return;
            }

            // Подключение к серверу
            try
            {
                _client = new TcpClient();
                _client.Connect(ipAddress, port);

                // Отправка имени пользователя на сервер
                var userName = UsernameTextBox.Text.Trim();
                var buffer = Encoding.Unicode.GetBytes($"CONNECT|{userName}");
                _client.GetStream().Write(buffer, 0, buffer.Length);

                // Запуск прослушивания сервера в отдельном потоке
                var task = Task.Factory.StartNew(ListenServer, TaskCreationOptions.LongRunning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            // Проверка ввода имени пользователя
            if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
            {
                MessageBox.Show("Введите имя пользователя");
                return;
            }

            // Получение порта из TextBox
            if (!int.TryParse(PortTextBox.Text, out var port))
            {
                MessageBox.Show("Введите корректный порт");
                return;
            }

            try
            {
                // Создание TcpListener на указанном порту
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();

                // Запуск прослушивания клиентов в отдельном потоке
                var task = Task.Factory.StartNew(ListenClients, TaskCreationOptions.LongRunning);

                // Отправка имени пользователя на сервер
                var userName = UsernameTextBox.Text.Trim();
                var buffer = Encoding.Unicode.GetBytes($"CONNECT|{userName}");
                var localClient = new TcpClient("localhost", port);
                localClient.GetStream().Write(buffer, 0, buffer.Length);
                // Отключение временного клиента
                localClient.Close();

                // Отключение кнопки создания сервера
                CreateButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private async void ListenClients()
        {
            try
            {
                while (true)
                {
                    // Ожидание подключения клиента
                    var client = await _listener.AcceptTcpClientAsync();

                    // Получение имени пользователя от клиента
                    var buffer = new byte[1024];
                    var stream = client.GetStream();
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    var message = Encoding.Unicode.GetString(buffer, 0, bytesRead);
                    var userName = message.Split('|')[1];

                    // Создание объекта пользователя
                    var user = new User
                    {
                        Name = userName,
                        Client = client,
                        ConnectedAt = DateTime.Now
                    };

                    // Добавление пользователя в список
                    _users.Add(user);

                    // Отправка сообщения об успешном подключении клиента
                    var connectedMessage = $"{userName} подключился к серверу";
                    BroadcastMessage(connectedMessage, user);

                    // Запуск потока для прослушивания сообщений от пользователя
                    var task = Task.Factory.StartNew(() => ListenUser(user), TaskCreationOptions.LongRunning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private async void ListenUser(User user)
        {
            try
            {
                var stream = user.Client.GetStream();
                var buffer = new byte[1024];

                while (true)
                {
                    // Ожидание сообщения от пользователя
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    var message = Encoding.Unicode.GetString(buffer, 0, bytesRead);

                    // Проверка на отключение пользователя
                    if (message.StartsWith("DISCONNECT"))
                    {
                        // Отправка сообщения об отключении пользователя
                        var disconnectedMessage = $"{user.Name} отключился от сервера";
                        BroadcastMessage(disconnectedMessage, user);

                        // Удаление пользователя из списка
                        _users.Remove(user);

                        // Закрытие соединения
                        user.Client.Close();
                        break;
                    }

                    // Отправка сообщения всем пользователям
                    BroadcastMessage(message, user);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private async void ListenServer()
        {
            try
            {
                var stream = _client.GetStream();
                var buffer = new byte[1024];

                while (true)
                {
                    // Ожидание сообщения от сервера
                    var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    var message = Encoding.Unicode.GetString(buffer, 0, bytesRead);

                    // Обработка сообщения от сервера
                    if (message.StartsWith("CONNECTED"))
                    {
                        // Отображение сообщения об успешном подключении к серверу
                        Dispatcher.Invoke(() => ChatTextBox.AppendText("Подключение к серверу выполнено успешно\n"));
                    }
                    else if (message.StartsWith("DISCONNECTED"))
                    {
                        // Отображение сообщения об отключении от сервера
                        Dispatcher.Invoke(() => ChatTextBox.AppendText("Отключение от сервера выполнено успешно\n"));

                        // Закрытие соединения
                        _client.Close();
                    }
                    else
                    {
                        // Отображение полученного сообщения в чате
                        Dispatcher.Invoke(() => ChatTextBox.AppendText($"{message}\n"));
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void SendMessage()
        {
            try
            {
                // Получение сообщения из текстового поля
                var message = MessageTextBox.Text;

                // Отправка сообщения на сервер
                var stream = _client.GetStream();
                var buffer = Encoding.Unicode.GetBytes(message);
                stream.Write(buffer, 0, buffer.Length);

                // Очистка текстового поля
                MessageTextBox.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void BroadcastMessage(string message, User sender)
        {
            try
            {
                // Отправка сообщения всем пользователям кроме отправителя
                foreach (var user in _users)
                {
                    if (user != sender)
                    {
                        var stream = user.Client.GetStream();
                        var buffer = Encoding.Unicode.GetBytes(message);
                        stream.Write(buffer, 0, buffer.Length);
                    }
                }

                // Отображение сообщения в чате
                Dispatcher.Invoke(() => ChatTextBox.AppendText($"{message}\n"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            ConnectToServer();
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            DisconnectFromServer();
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }
    }
}