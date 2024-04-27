using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Windows.Devices.Display;
using Windows.Devices.Display.Core;

namespace DisplayPathTest;

internal static class DeviceHelper
{
	public static void CheckDevices()
	{
		using DisplayManager manager = DisplayManager.Create(DisplayManagerOptions.None);
		DisplayState state = manager.TryReadCurrentStateForAllTargets().State;
		foreach (var view in state.Views)
		{
			foreach (var path in view.Paths)
			{
				DisplayMonitor? monitor = path.Target.TryGetMonitor();
				if (monitor is not null)
				{
					Console.WriteLine($"[{monitor.DisplayName}]");
					Console.WriteLine($"  Device ID           : {monitor.DeviceId}");

					var device = GetDisplayDevices().FirstOrDefault(x => x.monitorDeviceId == monitor.DeviceId);
					Console.WriteLine($"  GDI Name            : {device.displayDeviceName}");

					Console.WriteLine();
				}
			}
		}
	}

	public static IEnumerable<(IntPtr monitorHandle, string displayName, string deviceId, string deviceInstanceId)> EnumerateDisplayDeviceMonitorPairs()
	{
		var devices = GetDisplayDevices();

		foreach (var monitor in GetDisplayMonitors())
		{
			foreach (var device in devices.Where(x => x.displayDeviceName == monitor.displayDeviceName))
			{
				var deviceInstanceId = ConvertToDeviceInstanceId(device.monitorDeviceId);
				yield return (monitor.monitorHandle, device.displayDeviceName, device.monitorDeviceId, deviceInstanceId);
			}
		}
	}

	private static (string displayDeviceName, string monitorDeviceId)[] GetDisplayDevices()
	{
		var list = new List<(string, string)>();
		var size = (uint)Marshal.SizeOf<DISPLAY_DEVICE>();
		var display = new DISPLAY_DEVICE { cb = size };
		var monitor = new DISPLAY_DEVICE { cb = size };

		for (uint i = 0; EnumDisplayDevices(null, i, ref display, EDD_GET_DEVICE_INTERFACE_NAME); i++)
		{
			for (uint j = 0; EnumDisplayDevices(display.DeviceName, j, ref monitor, EDD_GET_DEVICE_INTERFACE_NAME); j++)
			{
				list.Add((display.DeviceName, monitor.DeviceID));
			}
		}
		return list.ToArray();
	}

	private static (string displayDeviceName, IntPtr monitorHandle)[] GetDisplayMonitors()
	{
		var list = new List<(string, IntPtr)>();
		var size = (uint)Marshal.SizeOf<MONITORINFOEX>();

		EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
			(monitorHandle, hdcMonitor, lprcMonitor, dwData) =>
			{
				var monitorInfo = new MONITORINFOEX { cbSize = size };
				if (GetMonitorInfo(monitorHandle, ref monitorInfo))
				{
					list.Add((monitorInfo.szDevice, monitorHandle));
				}
				return true;
			}, IntPtr.Zero);
		return list.ToArray();
	}

	private static string ConvertToDeviceInstanceId(string? deviceId)
	{
		if (!string.IsNullOrEmpty(deviceId))
		{
			var pattern = new Regex(@"\\\?\\DISPLAY#(?<hardware>\w+)#(?<instance>[\w|&]+)#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}");
			var match = pattern.Match(deviceId);
			if (match.Success)
			{
				return $@"DISPLAY\{match.Groups["hardware"]}\{match.Groups["instance"]}";
			}
		}
		return string.Empty;
	}

	#region Win32

	[DllImport("User32.dll", CharSet = CharSet.Ansi)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnumDisplayDevices(
		string? lpDevice,
		uint iDevNum,
		ref DISPLAY_DEVICE lpDisplayDevice,
		uint dwFlags);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
	private struct DISPLAY_DEVICE
	{
		public uint cb;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string DeviceName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DeviceString;

		public uint StateFlags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DeviceID;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DeviceKey;
	}

	private const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

	[DllImport("User32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnumDisplayMonitors(
		IntPtr hdc,
		IntPtr lprcClip,
		MonitorEnumProc lpfnEnum,
		IntPtr dwData);

	[return: MarshalAs(UnmanagedType.Bool)]
	private delegate bool MonitorEnumProc(
		IntPtr hMonitor,
		IntPtr hdcMonitor,
		IntPtr lprcMonitor,
		IntPtr dwData);

	[DllImport("User32.dll", CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetMonitorInfo(
		IntPtr hMonitor,
		ref MONITORINFOEX lpmi);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	public struct MONITORINFOEX
	{
		public uint cbSize;
		public RECT rcMonitor;
		public RECT rcWork;
		public uint dwFlags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string szDevice;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct RECT
	{
		public int left;
		public int top;
		public int right;
		public int bottom;
	}

	#endregion
}
