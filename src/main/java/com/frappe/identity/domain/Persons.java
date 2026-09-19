package com.frappe.identity.domain;

import com.frappe.platform.Result;

/** Port: the stored people. */
public interface Persons {

    /**
     * Stores a newly registered person with their recovery codes.
     *
     * @param person the registered person
     * @return the person, or {@link EmailTaken} when another person already has the address in any case
     */
    Result<Person, EmailTaken> add(Person person);

    /**
     * Whether a person has the address, in any case.
     *
     * @param email the address
     * @return whether it is registered
     */
    boolean isRegistered(EmailAddress email);

    /** The address already belongs to a person. */
    record EmailTaken() {}
}
