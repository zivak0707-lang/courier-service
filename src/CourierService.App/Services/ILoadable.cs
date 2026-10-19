using CommunityToolkit.Mvvm.Input;

namespace CourierService.App.Services;

/// <summary>
/// Сторінка, яка завантажує дані після відкриття (сервіс навігації запускає <see cref="LoadCommand"/>).
/// </summary>
public interface ILoadable
{
    /// <summary>Асинхронне завантаження даних сторінки.</summary>
    IAsyncRelayCommand LoadCommand { get; }
}
