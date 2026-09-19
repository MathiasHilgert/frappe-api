/**
 * Use cases: registers classes marked {@code @CommandUseCase} / {@code @QueryUseCase} as beans and wraps every call in
 * one {@code use_case} observation, outside the transaction, and a rollback of the use case's own transaction when it
 * returns a failure.
 */
package com.frappe.platform.infrastructure.usecase;
