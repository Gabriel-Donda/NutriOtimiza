using System.Threading.Tasks;
using Wpf.Ui.Controls;

namespace OtimizaPC.App.Services
{
    public interface IDialogService
    {
        /// <summary>Pede confirmação ao usuário; true se ele confirmou.</summary>
        Task<bool> ConfirmAsync(string title, string message, string confirmText);
    }

    public sealed class DialogService : IDialogService
    {
        public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
        {
            var box = new MessageBox
            {
                Title = title,
                Content = new System.Windows.Controls.TextBlock
                {
                    Text = message,
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    MaxWidth = 460,
                },
                PrimaryButtonText = confirmText,
                PrimaryButtonAppearance = ControlAppearance.Primary,
                CloseButtonText = "Cancelar",
            };
            return await box.ShowDialogAsync() == MessageBoxResult.Primary;
        }
    }
}
