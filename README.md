# Bosch GLM BLE Bridge

A minimal .NET console app that handles Bluetooth LE communication with a Bosch GLM 50-27CG.  
This code is the standalone “talk-to-the-GLM” piece—designed so you can integrate it into your Trimble Access app on a TSC7 tablet.

## Purpose

- **BLE link to Bosch GLM**  
  Sends the AutoSync command, listens for notifications, parses distance readings.
- **Integration stub**  
  Meant to be dropped into your larger Trimble Access–based workflow on a TSC7 device.  

> **Note:** This repo only contains the BLE communication logic. You’ll still need to wrap it in your Trimble Access add-in.

## How It Works

1. **Connect** via BluetoothLEDevice to your GLM’s MAC address.  
2. **Discover** the custom service & characteristic (UUIDs below).  
3. **Enable** Notify/Indicate on the characteristic.  
4. **Send** the AutoSync command (`C0 55 02 01 00 1A`).  
5. **Receive** data packets prefixed `C0 55 10 06…`, extract a 32-bit float at offset 7, print meters.  
6. **Clean up** on Ctrl+C.
