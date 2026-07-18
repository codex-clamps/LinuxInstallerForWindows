using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LinuxInstaller.Models;
using Avalonia.Controls;
using System.Collections.Generic;
using System;
using System.Linq;

namespace LinuxInstaller.ViewModels;

public partial class PartitionDialogViewModel : ObservableObject
{
    private const ulong PartitionAlignmentBytes = 1024UL * 1024UL;
    private readonly Window _dialogWindow;
    private readonly ulong _spaceStart;
    private readonly ulong _spaceSize;
    private bool _isUpdatingGeometry;

    [ObservableProperty]
    private PlannedPartition _targetPartition;

    [ObservableProperty]
    private decimal _size;

    [ObservableProperty]
    private string _selectedSizeUnit = AvailableSizeUnits[0];

    [ObservableProperty]
    private decimal _freeSpaceBefore = 0; // Decimal value for free space before

    [ObservableProperty]
    private decimal _freeSpaceAfter = 0; // Decimal value for free space after

    [ObservableProperty]
    private decimal _maxSize;

    [ObservableProperty]
    private bool _isNew = false;

    [ObservableProperty]
    private string _title;

    public static List<string> AvailableFileSystems { get; } = [.. Enum.GetValues<FileSystem>().Select(FS.ToString)];
    public static List<string> AvailableSizeUnits { get; } = ["MB", "GB"];

    public PartitionDialogViewModel(Window dialogWindow, ChartSpace space, int index = 0, bool hasRoot = false)
    {
        _dialogWindow = dialogWindow;
        _spaceStart = space.Start;
        _spaceSize = space.Size;
        SelectedSizeUnit = "MB";

        if (space is ChartPartition ps)
        {
            TargetPartition = ps.Partition.Clone();
            Title = "Edit Partition";
        }
        else
        {
            IsNew = true;
            TargetPartition = new PlannedPartition()
            {
                Id = PlannedPartition.CreateId(),
                Name = "New Partition " + (index == 0 ? "" : $"{index}"),
                StartOffset = space.Start,
                Size = space.Size,
                FileSystem = FileSystem.LINUX,
                IsSystem = false,
                MountPoint = hasRoot ? "" : "/"
            };
            Title = "Add New Partition";
        }

        var max = BytesToUnit(TargetPartition.Size, SelectedSizeUnit);
        _size = max;
        _maxSize = max;
    }

    partial void OnSelectedSizeUnitChanged(string? oldValue, string newValue)
    {
        if (oldValue == null || _isUpdatingGeometry)
        {
            return;
        }

        _isUpdatingGeometry = true;
        MaxSize = ConvertUnits(MaxSize, oldValue, newValue);
        FreeSpaceBefore = ConvertUnits(FreeSpaceBefore, oldValue, newValue);
        Size = ConvertUnits(Size, oldValue, newValue);
        FreeSpaceAfter = ConvertUnits(FreeSpaceAfter, oldValue, newValue);
        _isUpdatingGeometry = false;
        NotifyGeometryChanged();
    }

    partial void OnFreeSpaceBeforeChanged(decimal oldValue, decimal newValue)
    {
        if (_isUpdatingGeometry)
        {
            return;
        }

        _isUpdatingGeometry = true;
        Size = MaxSize - newValue - FreeSpaceAfter;
        _isUpdatingGeometry = false;
        NotifyGeometryChanged();
    }

    partial void OnFreeSpaceAfterChanged(decimal oldValue, decimal newValue)
    {
        if (_isUpdatingGeometry)
        {
            return;
        }

        _isUpdatingGeometry = true;
        Size = MaxSize - FreeSpaceBefore - newValue;
        _isUpdatingGeometry = false;
        NotifyGeometryChanged();
    }

    partial void OnSizeChanged(decimal oldValue, decimal newValue)
    {
        if (_isUpdatingGeometry)
        {
            return;
        }

        _isUpdatingGeometry = true;
        FreeSpaceAfter = MaxSize - FreeSpaceBefore - newValue;
        _isUpdatingGeometry = false;
        NotifyGeometryChanged();
    }

    private static decimal BytesToUnit(ulong bytes, string unit) => unit switch
    {
        "MB" => bytes / (decimal)(1024 * 1024),
        "GB" => bytes / (decimal)(1024 * 1024 * 1024),
        _ => throw new ArgumentOutOfRangeException(nameof(unit), $"Unknown unit: {unit}")
    };

    private static decimal ConvertUnits(decimal value, string oldUnit, string newUnit)
    {
        var oldFactor = GetUnitFactor(oldUnit);
        var newFactor = GetUnitFactor(newUnit);
        return value * oldFactor / newFactor;
    }

    private static decimal GetUnitFactor(string unit) => unit switch
    {
        "MB" => 1024m * 1024m,
        "GB" => 1024m * 1024m * 1024m,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), $"Unknown unit: {unit}")
    };

    public static ulong ConvertToAlignedBytes(decimal value, string unit)
    {
        if (!TryConvertToAlignedBytes(value, unit, out var bytes))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "The size must be non-negative and fit in an unsigned 64-bit byte count.");
        }

        return bytes;
    }

    private static bool TryConvertToAlignedBytes(decimal value, string unit, out ulong bytes)
    {
        bytes = 0;
        if (value < 0)
        {
            return false;
        }

        try
        {
            var rawBytes = value * GetUnitFactor(unit);
            var alignedBytes = decimal.Floor(rawBytes / PartitionAlignmentBytes) *
                PartitionAlignmentBytes;
            if (alignedBytes > ulong.MaxValue)
            {
                return false;
            }

            bytes = (ulong)alignedBytes;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public bool CanSave => TryGetPartitionGeometry(out _, out _);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Ok()
    {
        if (!TryGetPartitionGeometry(out var startOffset, out var size))
        {
            return;
        }

        TargetPartition.StartOffset = startOffset;
        TargetPartition.Size = size;
        _dialogWindow.Close(TargetPartition);
    }

    private bool TryGetPartitionGeometry(out ulong startOffset, out ulong size)
    {
        startOffset = _spaceStart;
        size = 0;
        if (_spaceSize == 0 ||
            _spaceStart % PartitionAlignmentBytes != 0 ||
            _spaceSize % PartitionAlignmentBytes != 0 ||
            Size <= 0 ||
            FreeSpaceBefore < 0 ||
            FreeSpaceAfter < 0 ||
            !TryConvertToAlignedBytes(Size, SelectedSizeUnit, out size))
        {
            return false;
        }

        ulong freeSpaceBefore = 0;
        if (IsNew &&
            !TryConvertToAlignedBytes(
                FreeSpaceBefore,
                SelectedSizeUnit,
                out freeSpaceBefore))
        {
            return false;
        }

        if (size == 0 ||
            freeSpaceBefore > _spaceSize ||
            size > _spaceSize - freeSpaceBefore ||
            startOffset > ulong.MaxValue - freeSpaceBefore)
        {
            return false;
        }

        startOffset += freeSpaceBefore;
        return startOffset % PartitionAlignmentBytes == 0 &&
            size % PartitionAlignmentBytes == 0;
    }

    private void NotifyGeometryChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        OkCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Cancel()
    {
        _dialogWindow.Close(null);
    }
}
