namespace Frappe.Access.Application;

/// <summary>
/// What the use cases running in this scope owe and have done: the stack of the requests the gate admitted, because a request can run inside
/// another one's scope (the session authentication runs its own use case before the route's), and each one's resource checks. Scoped: it
/// belongs to one request of the host and is never shared.
/// </summary>
internal sealed class AccessScope
{
    private readonly Stack<Frame> _frames = [];

    /// <summary>The name of the request being decided; <c>none</c> outside one.</summary>
    public string CurrentRequest => _frames.TryPeek(out var frame) ? frame.Request : "none";

    /// <summary>The gate admitted <paramref name="declaration"/>'s request.</summary>
    public void Enter(UseCaseDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        _frames.Push(new Frame(declaration.Name, declaration.ActsOn));
    }

    /// <summary>The innermost request ended; the resource types it owed and never checked (empty when it kept its obligations).</summary>
    public IReadOnlyCollection<Type> Leave()
    {
        var frame = _frames.Pop();
        if (frame.Owed.Count == 0)
        {
            return [];
        }

        return [.. frame.Owed.Where(owed => frame.Checked?.Contains(owed) != true)];
    }

    /// <summary>The handler of the innermost request checked a resource of type <paramref name="resource"/>.</summary>
    public void Checked(Type resource)
    {
        if (_frames.TryPeek(out var frame))
        {
            (frame.Checked ??= []).Add(resource);
        }
    }

    /// <summary>One admitted request: its name, the resource types it owes a check for and the ones checked so far.</summary>
    private sealed class Frame(string request, IReadOnlyCollection<Type> owed)
    {
        public string Request { get; } = request;

        public IReadOnlyCollection<Type> Owed { get; } = owed;

        public HashSet<Type>? Checked { get; set; }
    }
}
