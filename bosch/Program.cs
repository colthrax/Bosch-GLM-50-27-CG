using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace GlmConsoleApp
{
    class Program
    {
        const ulong GlmAddress = 0x546C0EA48576UL;   // Din GLM MAC
        static readonly Guid ServiceUuid = Guid.Parse("02a6c0d0-0451-4000-b000-fb3210111989");
        static readonly Guid CharUuid = Guid.Parse("02a6c0d1-0451-4000-b000-fb3210111989");

        // Kommandon prefix
        static readonly byte[] AutoSyncCmd = { 0xC0, 0x55, 0x02, 0x01, 0x00, 0x1A };
        static readonly byte[] DataPrefix = { 0xC0, 0x55, 0x10, 0x06 };

        static async Task Main()
        {
            Console.WriteLine("Tryck mätknappen på GLM och vänta...");
            await Task.Delay(2000);

            using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(GlmAddress);
            if (device == null)
            {
                Console.WriteLine("Enhet ej funnen.");
                return;
            }
            Console.WriteLine("Enhet ansluten.");

            var svcResult = await device.GetGattServicesForUuidAsync(ServiceUuid);
            var service = svcResult.Services.FirstOrDefault();
            if (service == null)
            {
                Console.WriteLine($"Service {ServiceUuid} ej funnen.");
                return;
            }

            var charResult = await service.GetCharacteristicsForUuidAsync(CharUuid);
            var characteristic = charResult.Characteristics.FirstOrDefault();
            if (characteristic == null)
            {
                Console.WriteLine($"Karaktäristik {CharUuid} ej funnen.");
                return;
            }

            var properties = characteristic.CharacteristicProperties;
            var cccdValue = properties.HasFlag(GattCharacteristicProperties.Indicate)
                ? GattClientCharacteristicConfigurationDescriptorValue.Indicate
                : GattClientCharacteristicConfigurationDescriptorValue.Notify;

            characteristic.ValueChanged += Characteristic_ValueChanged;
            var cfgStatus = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue);
            Console.WriteLine(cfgStatus == GattCommunicationStatus.Success
                ? $"CCCD aktiverad ({cccdValue})."
                : $"⚠️ Kunde ej aktivera CCCD ({cccdValue}): {cfgStatus}");

            using (var writer = new DataWriter())
            {
                writer.WriteBytes(AutoSyncCmd);
                var writeStatus = await characteristic.WriteValueAsync(writer.DetachBuffer());
                Console.WriteLine(writeStatus == GattCommunicationStatus.Success
                    ? "AutoSync-kommandot skickat."
                    : $"⚠️ Skrivfel: {writeStatus}");
            }

            // Hantera Ctrl+C för snygg avslutning
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine("Väntar på mätvärden (Ctrl+C för att avsluta)...");
            try { await Task.Delay(Timeout.Infinite, cts.Token); } catch { }
        }

        private static void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                var buffer = args.CharacteristicValue;
                var data = new byte[buffer.Length];
                DataReader.FromBuffer(buffer).ReadBytes(data);

                if (data.Length > 11 && data.Take(4).SequenceEqual(DataPrefix))
                {
                    float meters = BitConverter.ToSingle(data, 7);
                    string text = meters.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                    Console.WriteLine($"Mätning: {text} m");

                    // Kort paus för att säkerställa fokus
                    Thread.Sleep(50);

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Data-tolkningsfel: " + ex.Message);
            }
        }

    }
}