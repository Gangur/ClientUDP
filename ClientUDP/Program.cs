using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClientUDP
{
    class Program
    {
        public static Random random { get; set; }

        public static IPAddress Address { get; set; }
        public static int Port { get; set; }

        // Key - значение,</br> Value - частота встречи
        public static ConcurrentDictionary<long, long> NumClasses { get; set; }
        public static ulong LostPackages { get; set; }

        public static ulong Counter { get; set; }

        static void Main(string[] args)
        {
            try
            {
                InitApp();
                Task.Run(() => ReceiveMessage());
                while (true)
                {
                    if(Console.ReadKey().Key == ConsoleKey.Enter)
                        GetInfo();
                }
            }
            catch (Exception ex){Console.WriteLine(ex.Message);}
        }

        private static void ReceiveMessage()
        {
            UdpClient server = new UdpClient(Port);
            server.JoinMulticastGroup(Address, 20);
            IPEndPoint remoteIp = null;
            try
            {
                byte[] data;
                byte conrol_byte;
                string[] mes;
                long key;
                while (true)
                {
                    data = server.Receive(ref remoteIp);
                    conrol_byte = data[data.Length-1];
                    Array.Resize(ref data, data.Length - 1);
                    if(conrol_byte == GetControlByte(data))
                    {
                        mes = Encoding.ASCII.GetString(data).Split(',');
                        key = Convert.ToInt64(mes[0]);

                        if (Counter == 0) { Counter = Convert.ToUInt64(mes[1]); }
                        else
                        {
                            if (Counter+1 != Convert.ToUInt64(mes[1]))
                            {
                                LostPackages += (Convert.ToUInt64(mes[1]) - Counter);
                                Counter = Convert.ToUInt64(mes[1]);
                                continue;
                            }
                            else
                                Counter = Convert.ToUInt64(mes[1]);
                        }

                        if (NumClasses.ContainsKey(key))
                        {
                            if (!NumClasses.TryUpdate(key, NumClasses[key] + 1, NumClasses[key])) // Думаю эти условия никогда не сработает, но я бы оставил
                                LostPackages++;
                        }
                        else
                            if (!NumClasses.TryAdd(key, 1))
                                LostPackages++;
                    }
                    else
                    {
                        LostPackages++;
                    }

                    // Имитация потерь
                    //if(random.Next(0,1000) == 13)
                    //    Thread.Sleep(100);
                }
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
            finally { 
                server.Close();
                ReceiveMessage();
            }
        }

        private static void InitApp()
        {
            var map = new ExeConfigurationFileMap { ExeConfigFilename = "client.config" };
            var config = ConfigurationManager.OpenMappedExeConfiguration(map, ConfigurationUserLevel.None);

            string group = config.AppSettings.Settings["m_group"].Value;
            Address = IPAddress.Parse(group);
            Port = Convert.ToInt32(config.AppSettings.Settings["port"].Value);
            NumClasses = new ConcurrentDictionary<long, long>();
            random = new Random();

            Console.WriteLine("Client started...");
        }

        private static void GetInfo()
        {
            Console.WriteLine($"\nНачало расчетов...");

            SortedDictionary<long, long> classes 
                = new SortedDictionary<long, long>(NumClasses);

            long total_count = 0; //Всего пакетов
            var ave_pair = new KeyValuePair<long, long>(); //условная средняя / Мода
            foreach (KeyValuePair<long, long> pair in classes)
            {
                total_count += pair.Value;
                if (pair.Value > ave_pair.Value)
                    ave_pair = pair;
            }

            double b = 0; //Среднее отклонение от условной средней;
            foreach (KeyValuePair<long, long> pair in classes)
                b += (pair.Key - ave_pair.Key) * pair.Value;
            b = b / total_count;
            double ave = ave_pair.Key + b;//Среднее арифметическое

            double q = 0; //Стандартное откланение
            double median = -1; //Медиана
            bool flag = total_count % 2 == 0; //Четность
            long counter = flag ? total_count / 2 : total_count / 2 + 1;
            foreach (KeyValuePair<long, long> pair in classes)
            {
                for (int i = 0; i < pair.Value; i++)
                    q += Math.Pow(pair.Key - ave, 2);

                counter -= pair.Value;
                if (flag && median > 0)
                {
                    median = (median + pair.Key) / 2;
                    flag = false;
                }

                if (counter <= 0 && median < 0)
                {
                    median = pair.Key;
                    flag = flag && counter == 0;
                }
            }
            q = q / (total_count - 1);
            q = Math.Sqrt(q);

            Console.WriteLine();
            Console.WriteLine($"Медиана: {median}");
            Console.WriteLine($"Мода: {ave_pair.Key}");
            Console.WriteLine($"Среднее отклонение от условной средней: {b}");
            Console.WriteLine($"Среднее арифметическое: {ave}");
            Console.WriteLine($"Стандартное откланение: {q}");

            Console.WriteLine($"Количество пакетов: {total_count}");

            Console.WriteLine();
            Console.WriteLine($"Потеряно пакетов: {LostPackages}");
            Console.WriteLine();
        }

        private static byte GetControlByte(byte[] data)
        {
            byte b = data[0];
            for (int i = 1; i < data.Length; i++)
                b ^= data[i];
            return b;
        }
    }
}
