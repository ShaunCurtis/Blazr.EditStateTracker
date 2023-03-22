using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Blazr.EditStateTracker.Data
{
    public class EditStateTracker : ComponentBase, IDisposable
    {
        [CascadingParameter] private EditContext _editContext { get; set; } = default!;

        private EditStateStore _store = default!;

        public EditStateTracker() { }

        protected override void OnInitialized()
        {
            ArgumentNullException.ThrowIfNull(_editContext);
            _store = new(_editContext);
            ArgumentNullException.ThrowIfNull(_store);
            _editContext.OnFieldChanged += OnFieldChanged;
        }

        private void OnFieldChanged(object? sender, FieldChangedEventArgs e)
        {
            _store.Update(e);
        }

        public bool IsDirty => _store.IsDirty();

        public void Dispose()
        {
            _editContext.OnFieldChanged -= OnFieldChanged;
        }
    }
}
