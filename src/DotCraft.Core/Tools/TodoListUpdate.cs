using DotCraft.Memory;

namespace DotCraft.Tools;

internal sealed record TodoListUpdate(bool Advance, IReadOnlyList<int> Done, int? Start)
{
    public bool IsEmpty => !Advance && Done.Count == 0 && Start is null;

    public bool HasInvalidPosition(int count) =>
        (Start is { } start ? Done.Append(start) : Done).Any(position => position < 1 || position > count);

    public void Apply(IReadOnlyList<PlanTodo> todos)
    {
        foreach (var position in Done)
            todos[position - 1].Status = PlanTodoStatus.Completed;
        if (Start is { } start)
            todos[start - 1].Status = PlanTodoStatus.InProgress;
        if (!Advance)
            return;

        var current = IndexOf(todos, PlanTodoStatus.InProgress, 0);
        if (current >= 0)
            todos[current].Status = PlanTodoStatus.Completed;
        var next = IndexOf(todos, PlanTodoStatus.Pending, current + 1);
        if (next < 0)
            next = IndexOf(todos, PlanTodoStatus.Pending, 0);
        if (next >= 0)
            todos[next].Status = PlanTodoStatus.InProgress;
    }

    public static string DescribeReplacement(IReadOnlyList<PlanTodo> todos, bool updateEmpty)
    {
        var text = $"Task list shown ({todos.Count} steps).";
        if (todos.Count(static todo => todo.Status == PlanTodoStatus.InProgress) > 1)
            text += " Keep only one step in_progress.";
        if (!updateEmpty)
            text += " todos sets every step's status, so the advance, done, or start sent with it changed nothing. Send that update again without todos if it still applies.";
        return text;
    }

    public static string DescribeProgress(IReadOnlyList<PlanTodo> todos)
    {
        var active = todos
            .Select(static (todo, index) => (todo, position: index + 1))
            .Where(static entry => entry.todo.Status == PlanTodoStatus.InProgress)
            .Select(static entry => entry.position)
            .ToArray();
        if (active.Length == 1)
            return $"step {active[0]} of {todos.Count} is in progress";
        if (active.Length > 1)
            return $"steps {string.Join(", ", active)} of {todos.Count} are in progress. Keep one and send done for any you finished";

        var completed = todos.Count(static todo => todo.Status == PlanTodoStatus.Completed);
        return completed == todos.Count
            ? "every step is done"
            : $"no step is in progress ({completed} of {todos.Count} done). Send start to begin one";
    }

    private static int IndexOf(IReadOnlyList<PlanTodo> todos, string status, int from)
    {
        for (var i = from; i < todos.Count; i++)
        {
            if (todos[i].Status == status)
                return i;
        }

        return -1;
    }
}
