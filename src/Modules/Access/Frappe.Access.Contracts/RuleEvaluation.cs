namespace Frappe.Access.Contracts;

/// <summary>The evaluation of one rule: what a rule is once it has been declared, whatever kind of rule it was.</summary>
public delegate ValueTask<Verdict> RuleEvaluation(RuleContext context, CancellationToken cancellationToken);
