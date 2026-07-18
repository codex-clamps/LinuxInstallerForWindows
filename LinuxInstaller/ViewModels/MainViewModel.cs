using CommunityToolkit.Mvvm.ComponentModel;
using LinuxInstaller.Services;
using LinuxInstaller.ViewModels.Interfaces;
using System;
using System.Collections.Generic;
using System.Reactive.Linq;

namespace LinuxInstaller.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly NavigationService _navigationService;

    private NavigatableViewModelBase _currentContent;
    public NavigatableViewModelBase CurrentContent
    {
        get => _currentContent;
        set => SetProperty(ref _currentContent, value);
    }

    public MainViewModel(
        WorkflowSelectionViewModel workflowSelectionViewModel,
        DistroPickerViewModel distroPickerViewModel,
        PartitionEditorViewModel partitionEditorViewModel,
        UserCreationViewModel userCreationViewModel,
        InstallationSummaryViewModel installationSummaryViewModel,
        InstallationProgressViewModel installationProgressViewModel,
        InstallationFinishViewModel installationFinishViewModel,
        LoadingViewModel loadingViewModel,
        NavigationService navigationService)
    {
        _navigationService = navigationService;

        List<KeyValuePair<string, NavigatableViewModelBase>> routes =
        [
            new("loading", loadingViewModel),
            new("workflowSelection", workflowSelectionViewModel),
            new("distroPicker", distroPickerViewModel),
            new("partitionEditor", partitionEditorViewModel),
            new("userCreation", userCreationViewModel),
            new("installationSummary", installationSummaryViewModel),
            new("installationProgress", installationProgressViewModel),
            new("installationFinish", installationFinishViewModel)
        ];

        _navigationService.SetupRoutes(routes);
        _currentContent = _navigationService.CurrentPage;

        _navigationService.CurrentPageIndexObservable
            .Subscribe(async _ =>
            {
                CurrentContent = _navigationService.CurrentPage;
                if (CurrentContent is PartitionEditorViewModel partitionEditor)
                {
                    await partitionEditor.ActivateAsync();
                }
                else if (CurrentContent is InstallationProgressViewModel progress)
                {
                    await progress.StartInstallationAsync();
                }
            });
    }
}
