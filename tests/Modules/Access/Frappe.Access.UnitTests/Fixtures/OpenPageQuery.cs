using Frappe.Access.Contracts;
using Frappe.UseCases;

namespace Frappe.Access.UnitTests.Fixtures;

/// <summary>A public query: it proves itself some other way.</summary>
public sealed record OpenPageQuery : IQuery<string>, IPublic;
