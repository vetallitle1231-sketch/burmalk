namespace Burmalk.Views
{
    /// <summary>Implemented by views that MainWindow re-refreshes from the monitor tick.</summary>
    internal interface IRefreshable
    {
        void Refresh();
    }
}

