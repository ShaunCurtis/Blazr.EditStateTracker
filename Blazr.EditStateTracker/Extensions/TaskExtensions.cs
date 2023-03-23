namespace Blazr.EditStateTracker.Extensions;

public static class TaskExtensions
{
    public static async Task Await(this Task task, Action? taskSuccess, Action<Exception>? taskFailure)
    {
        try
        {
            await task;
            taskSuccess?.Invoke();
        }
        catch (Exception ex)
        {
            taskFailure?.Invoke(ex);
        }
    }

    public static async ValueTask Await(this ValueTask task, Action? taskSuccess, Action<Exception>? taskFailure)
    {
        try
        {
            await task;
            taskSuccess?.Invoke();
        }
        catch (Exception ex)
        {
            taskFailure?.Invoke(ex);
        }
    }
}