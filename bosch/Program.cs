using System;
using System.Linq;
using System.Runtime.InteropServices;
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
                Console.WriteLine($"Karakteristisk {CharUuid} ej funnen.");
                return;
            }

            // Välj korrekt CCCD-värde (Notify eller Indicate)
            var props = characteristic.CharacteristicProperties;
            var cccdValue = props.HasFlag(GattCharacteristicProperties.Indicate)
                ? GattClientCharacteristicConfigurationDescriptorValue.Indicate
                : GattClientCharacteristicConfigurationDescriptorValue.Notify;

            // Aktivera notifieringar/indikeringar
            characteristic.ValueChanged += Characteristic_ValueChanged;
            try
            {
                var cfgStatus = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue);
                Console.WriteLine(cfgStatus == GattCommunicationStatus.Success
                    ? $"CCCD aktiverad ({cccdValue})."
                    : $"⚠️ Kunde ej aktivera CCCD ({cccdValue}): {cfgStatus}");
            }
            catch (COMException comEx)
            {
                Console.WriteLine($"⚠️ COMException vid CCCD: {comEx.Message}");
                // Fortsätt ändå
            }

            // Skicka AutoSyncEnable
            using var writer = new DataWriter();
            writer.WriteBytes(AutoSyncCmd);
            try
            {
                var writeStatus = await characteristic.WriteValueAsync(writer.DetachBuffer());
                Console.WriteLine(writeStatus == GattCommunicationStatus.Success
                    ? "AutoSync-kommandot skickat."
                    : $"⚠️ Skrivfel: {writeStatus}");
            }
            catch (COMException comEx)
            {
                Console.WriteLine($"⚠️ COMException vid WriteValue: {comEx.Message}");
            }

            Console.WriteLine("Väntar på mätvärden (Ctrl+C för att avsluta)...");
            await Task.Delay(-1);
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
                    int mm = (int)Math.Round(meters * 1000);
                    Console.WriteLine($"Mätning: {mm} mm");

                    SendString(mm.ToString());
                    SendKey(VK_RETURN);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Data-tolkningsfel: " + ex.Message);
            }
        }

        #region SendInput PInvoke
        const int INPUT_KEYBOARD = 1;
        const ushort VK_RETURN = 0x0D;
        const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public int type; public InputUnion U; }
        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        static void SendKey(ushort vk, uint flags = 0)
        {
            var down = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } } };
            var up = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags | KEYEVENTF_KEYUP } } };
            SendInput(1, new[] { down }, Marshal.SizeOf<INPUT>());
            SendInput(1, new[] { up }, Marshal.SizeOf<INPUT>());
        }
        static void SendString(string s)
        {
            foreach (char c in s)
            {
                short v = VkKeyScan(c);
                byte vk = (byte)(v & 0xFF);
                bool shift = (v & 0x0100) != 0;
                if (shift) SendKey(0x10);
                SendKey(vk);
                if (shift) SendKey(0x10, KEYEVENTF_KEYUP);
            }
        }
        [DllImport("user32.dll")] static extern short VkKeyScan(char ch);
        #endregion
    }
}
