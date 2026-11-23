
\restrict frappe

CREATE SCHEMA quartz;

CREATE TABLE quartz.qrtz_blob_triggers (
    sched_name text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    blob_data bytea
);

CREATE TABLE quartz.qrtz_calendars (
    sched_name text NOT NULL,
    calendar_name text NOT NULL,
    calendar bytea NOT NULL
);

CREATE TABLE quartz.qrtz_cron_triggers (
    sched_name text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    cron_expression text NOT NULL,
    time_zone_id text
);

CREATE TABLE quartz.qrtz_execution_history (
    sched_name text NOT NULL,
    entry_id text NOT NULL,
    instance_name text NOT NULL,
    job_name text NOT NULL,
    job_group text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    fired_time bigint NOT NULL,
    run_time bigint NOT NULL,
    succeeded boolean NOT NULL,
    error_message text,
    retry_attempt integer DEFAULT 0 NOT NULL,
    retry_scheduled boolean DEFAULT false NOT NULL,
    execution_log text,
    result integer,
    summary text,
    metrics text,
    manual boolean,
    fire_instance_id text,
    job_input text,
    job_input_too_large boolean
);

CREATE TABLE quartz.qrtz_fired_triggers (
    sched_name text NOT NULL,
    entry_id text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    instance_name text NOT NULL,
    fired_time bigint NOT NULL,
    sched_time bigint NOT NULL,
    priority integer NOT NULL,
    state text NOT NULL,
    job_name text,
    job_group text,
    is_nonconcurrent boolean NOT NULL,
    requests_recovery boolean,
    execution_group character varying(200),
    progress integer,
    progress_message character varying(250),
    interrupt_requested_time bigint
);

CREATE TABLE quartz.qrtz_job_details (
    sched_name text NOT NULL,
    job_name text NOT NULL,
    job_group text NOT NULL,
    description text,
    job_class_name text NOT NULL,
    is_durable boolean NOT NULL,
    is_nonconcurrent boolean NOT NULL,
    is_update_data boolean NOT NULL,
    requests_recovery boolean NOT NULL,
    job_data bytea
);

CREATE TABLE quartz.qrtz_job_status (
    sched_name text NOT NULL,
    job_group text NOT NULL,
    job_name text NOT NULL,
    first_fired_time bigint NOT NULL,
    last_fired_time bigint NOT NULL,
    last_result integer NOT NULL,
    last_run_time bigint NOT NULL,
    last_instance_name text NOT NULL,
    last_entry_id text,
    last_summary text,
    last_success_time bigint,
    last_failure_time bigint,
    last_failure_message text,
    consecutive_failures integer DEFAULT 0 NOT NULL,
    run_count bigint DEFAULT 0 NOT NULL,
    failure_count bigint DEFAULT 0 NOT NULL
);

CREATE TABLE quartz.qrtz_locks (
    sched_name text NOT NULL,
    lock_name text NOT NULL
);

CREATE TABLE quartz.qrtz_misfire_history (
    sched_name text NOT NULL,
    entry_id text NOT NULL,
    instance_name text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    job_name text,
    job_group text,
    misfire_time bigint NOT NULL,
    sched_time bigint,
    reason integer
);

CREATE TABLE quartz.qrtz_paused_job_grps (
    sched_name text NOT NULL,
    job_group text NOT NULL,
    pause_reason character varying(250),
    paused_by character varying(200),
    paused_at bigint
);

CREATE TABLE quartz.qrtz_paused_trigger_grps (
    sched_name text NOT NULL,
    trigger_group text NOT NULL,
    pause_reason character varying(250),
    paused_by character varying(200),
    paused_at bigint
);

CREATE TABLE quartz.qrtz_scheduler_state (
    sched_name text NOT NULL,
    instance_name text NOT NULL,
    last_checkin_time bigint NOT NULL,
    checkin_interval bigint NOT NULL,
    quartz_version character varying(20)
);

CREATE TABLE quartz.qrtz_simple_triggers (
    sched_name text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    repeat_count bigint NOT NULL,
    repeat_interval bigint NOT NULL,
    times_triggered bigint NOT NULL
);

CREATE TABLE quartz.qrtz_simprop_triggers (
    sched_name text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    str_prop_1 text,
    str_prop_2 text,
    str_prop_3 text,
    int_prop_1 integer,
    int_prop_2 integer,
    long_prop_1 bigint,
    long_prop_2 bigint,
    dec_prop_1 numeric,
    dec_prop_2 numeric,
    bool_prop_1 boolean,
    bool_prop_2 boolean,
    time_zone_id text
);

CREATE TABLE quartz.qrtz_triggers (
    sched_name text NOT NULL,
    trigger_name text NOT NULL,
    trigger_group text NOT NULL,
    job_name text NOT NULL,
    job_group text NOT NULL,
    description text,
    next_fire_time bigint,
    prev_fire_time bigint,
    priority integer,
    trigger_state text NOT NULL,
    trigger_type text NOT NULL,
    start_time bigint NOT NULL,
    end_time bigint,
    calendar_name text,
    misfire_instr smallint,
    misfire_orig_fire_time bigint,
    execution_group character varying(200),
    preferred_node character varying(200),
    preferred_node_auto boolean DEFAULT false NOT NULL,
    retry_policy character varying(250),
    retry_attempt integer,
    continues_trigger_name text,
    continues_trigger_group text,
    continuation_condition integer,
    overlap_policy integer,
    pause_reason character varying(250),
    paused_by character varying(200),
    paused_at bigint,
    misfire_threshold bigint,
    fixed_delay boolean,
    job_data bytea
);

ALTER TABLE ONLY quartz.qrtz_blob_triggers
    ADD CONSTRAINT qrtz_blob_triggers_pkey PRIMARY KEY (sched_name, trigger_name, trigger_group);

ALTER TABLE ONLY quartz.qrtz_calendars
    ADD CONSTRAINT qrtz_calendars_pkey PRIMARY KEY (sched_name, calendar_name);

ALTER TABLE ONLY quartz.qrtz_cron_triggers
    ADD CONSTRAINT qrtz_cron_triggers_pkey PRIMARY KEY (sched_name, trigger_name, trigger_group);

ALTER TABLE ONLY quartz.qrtz_execution_history
    ADD CONSTRAINT qrtz_execution_history_pkey PRIMARY KEY (sched_name, entry_id);

ALTER TABLE ONLY quartz.qrtz_fired_triggers
    ADD CONSTRAINT qrtz_fired_triggers_pkey PRIMARY KEY (sched_name, entry_id);

ALTER TABLE ONLY quartz.qrtz_job_details
    ADD CONSTRAINT qrtz_job_details_pkey PRIMARY KEY (sched_name, job_name, job_group);

ALTER TABLE ONLY quartz.qrtz_job_status
    ADD CONSTRAINT qrtz_job_status_pkey PRIMARY KEY (sched_name, job_group, job_name);

ALTER TABLE ONLY quartz.qrtz_locks
    ADD CONSTRAINT qrtz_locks_pkey PRIMARY KEY (sched_name, lock_name);

ALTER TABLE ONLY quartz.qrtz_misfire_history
    ADD CONSTRAINT qrtz_misfire_history_pkey PRIMARY KEY (sched_name, entry_id);

ALTER TABLE ONLY quartz.qrtz_paused_job_grps
    ADD CONSTRAINT qrtz_paused_job_grps_pkey PRIMARY KEY (sched_name, job_group);

ALTER TABLE ONLY quartz.qrtz_paused_trigger_grps
    ADD CONSTRAINT qrtz_paused_trigger_grps_pkey PRIMARY KEY (sched_name, trigger_group);

ALTER TABLE ONLY quartz.qrtz_scheduler_state
    ADD CONSTRAINT qrtz_scheduler_state_pkey PRIMARY KEY (sched_name, instance_name);

ALTER TABLE ONLY quartz.qrtz_simple_triggers
    ADD CONSTRAINT qrtz_simple_triggers_pkey PRIMARY KEY (sched_name, trigger_name, trigger_group);

ALTER TABLE ONLY quartz.qrtz_simprop_triggers
    ADD CONSTRAINT qrtz_simprop_triggers_pkey PRIMARY KEY (sched_name, trigger_name, trigger_group);

ALTER TABLE ONLY quartz.qrtz_triggers
    ADD CONSTRAINT qrtz_triggers_pkey PRIMARY KEY (sched_name, trigger_name, trigger_group);

CREATE INDEX idx_qrtz_eh_fire_inst ON quartz.qrtz_execution_history USING btree (sched_name, fire_instance_id);

CREATE INDEX idx_qrtz_eh_fired_time ON quartz.qrtz_execution_history USING btree (sched_name, fired_time);

CREATE INDEX idx_qrtz_eh_inst ON quartz.qrtz_execution_history USING btree (sched_name, instance_name);

CREATE INDEX idx_qrtz_eh_job_time ON quartz.qrtz_execution_history USING btree (sched_name, job_group, job_name, fired_time);

CREATE INDEX idx_qrtz_eh_trig_time ON quartz.qrtz_execution_history USING btree (sched_name, trigger_group, trigger_name, fired_time);

CREATE INDEX idx_qrtz_ft_inst_job_req_rcvry ON quartz.qrtz_fired_triggers USING btree (sched_name, instance_name, requests_recovery);

CREATE INDEX idx_qrtz_ft_j_g ON quartz.qrtz_fired_triggers USING btree (sched_name, job_name, job_group);

CREATE INDEX idx_qrtz_ft_t_g ON quartz.qrtz_fired_triggers USING btree (sched_name, trigger_name, trigger_group);

CREATE INDEX idx_qrtz_j_g_n ON quartz.qrtz_job_details USING btree (sched_name, job_group, job_name);

CREATE INDEX idx_qrtz_mh_inst ON quartz.qrtz_misfire_history USING btree (sched_name, instance_name);

CREATE INDEX idx_qrtz_mh_misfire_time ON quartz.qrtz_misfire_history USING btree (sched_name, misfire_time);

CREATE INDEX idx_qrtz_t_c ON quartz.qrtz_triggers USING btree (sched_name, calendar_name);

CREATE INDEX idx_qrtz_t_g_n ON quartz.qrtz_triggers USING btree (sched_name, trigger_group, trigger_name);

CREATE INDEX idx_qrtz_t_j ON quartz.qrtz_triggers USING btree (sched_name, job_name, job_group);

CREATE INDEX idx_qrtz_t_nft_st ON quartz.qrtz_triggers USING btree (sched_name, trigger_state, next_fire_time, priority DESC, misfire_instr);

ALTER TABLE ONLY quartz.qrtz_blob_triggers
    ADD CONSTRAINT qrtz_blob_triggers_sched_name_trigger_name_trigger_group_fkey FOREIGN KEY (sched_name, trigger_name, trigger_group) REFERENCES quartz.qrtz_triggers(sched_name, trigger_name, trigger_group) ON DELETE CASCADE;

ALTER TABLE ONLY quartz.qrtz_cron_triggers
    ADD CONSTRAINT qrtz_cron_triggers_sched_name_trigger_name_trigger_group_fkey FOREIGN KEY (sched_name, trigger_name, trigger_group) REFERENCES quartz.qrtz_triggers(sched_name, trigger_name, trigger_group) ON DELETE CASCADE;

ALTER TABLE ONLY quartz.qrtz_simple_triggers
    ADD CONSTRAINT qrtz_simple_triggers_sched_name_trigger_name_trigger_group_fkey FOREIGN KEY (sched_name, trigger_name, trigger_group) REFERENCES quartz.qrtz_triggers(sched_name, trigger_name, trigger_group) ON DELETE CASCADE;

ALTER TABLE ONLY quartz.qrtz_simprop_triggers
    ADD CONSTRAINT qrtz_simprop_triggers_sched_name_trigger_name_trigger_grou_fkey FOREIGN KEY (sched_name, trigger_name, trigger_group) REFERENCES quartz.qrtz_triggers(sched_name, trigger_name, trigger_group) ON DELETE CASCADE;

ALTER TABLE ONLY quartz.qrtz_triggers
    ADD CONSTRAINT qrtz_triggers_sched_name_job_name_job_group_fkey FOREIGN KEY (sched_name, job_name, job_group) REFERENCES quartz.qrtz_job_details(sched_name, job_name, job_group);

GRANT USAGE ON SCHEMA quartz TO frappe_scheduler;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_blob_triggers TO frappe_scheduler;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_calendars TO frappe_scheduler;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_cron_triggers TO frappe_scheduler;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_execution_history TO frappe_scheduler;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_fired_triggers TO frappe_scheduler;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_job_details TO frappe_scheduler;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_job_status TO frappe_scheduler;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_locks TO frappe_scheduler;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_misfire_history TO frappe_scheduler;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_paused_job_grps TO frappe_scheduler;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_paused_trigger_grps TO frappe_scheduler;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_scheduler_state TO frappe_scheduler;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_simple_triggers TO frappe_scheduler;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_simprop_triggers TO frappe_scheduler;

GRANT SELECT,INSERT,DELETE,UPDATE ON TABLE quartz.qrtz_triggers TO frappe_scheduler;

ALTER DEFAULT PRIVILEGES FOR ROLE frappe_owner IN SCHEMA quartz GRANT ALL ON SEQUENCES TO frappe_scheduler;

ALTER DEFAULT PRIVILEGES FOR ROLE frappe_owner IN SCHEMA quartz GRANT SELECT,INSERT,DELETE,UPDATE ON TABLES TO frappe_scheduler;

\unrestrict frappe

