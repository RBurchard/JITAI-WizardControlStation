using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OcdWizard.ControlStation.Database;
using OcdWizard.ControlStation.Models;
using OcdWizard.ControlStation.Services;

namespace OcdWizard.ControlStation.ViewModels;

public partial class TriviaEditorViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly List<DeviceConnection> _devices;

    [ObservableProperty] private string _pushStatus = "";
    [ObservableProperty] private TriviaQuestion? _selectedQuestion;

    public ObservableCollection<TriviaQuestion> Questions { get; } = [];

    public TriviaEditorViewModel(DatabaseService db, List<DeviceConnection> devices)
    {
        _db = db;
        _devices = devices;
    }

    public async Task LoadAsync()
    {
        var saved = await _db.LoadTriviaQuestionsAsync();
        Questions.Clear();
        foreach (var q in saved) Questions.Add(q);
        if (Questions.Count == 0) AddQuestion();
    }

    [RelayCommand]
    private void AddQuestion()
    {
        var q = new TriviaQuestion
        {
            Question = "New question?",
            Answers = ["Answer A", "Answer B", "Answer C", "Answer D"],
            CorrectIndex = 0
        };
        Questions.Add(q);
        SelectedQuestion = q;
    }

    [RelayCommand]
    private void RemoveQuestion(TriviaQuestion? q)
    {
        if (q != null) Questions.Remove(q);
        SelectedQuestion = Questions.Count > 0 ? Questions[0] : null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await _db.SaveTriviaQuestionsAsync(Questions);
        PushStatus = "Saved locally.";
    }

    [RelayCommand]
    private async Task PushToDevicesAsync()
    {
        await _db.SaveTriviaQuestionsAsync(Questions);
        int sent = 0;
        foreach (var device in _devices)
            if (await device.PushTriviaAsync([.. Questions])) sent++;
        PushStatus = $"Pushed to {sent}/{_devices.Count} device(s).";
    }
}
