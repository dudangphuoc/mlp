-- StreamBroker (20/02 §5.5, 20/10 §2.7): every change a user may be watching → NOTIFY mlp_ws '<workspace_id>'.
-- Every api replica LISTENs on the same channel, so Watch* streams work with multiple api instances; the payload is only a key, data is still read from the tables.
CREATE OR REPLACE FUNCTION platform.notify_ws() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF NEW.workspace_id IS NOT NULL THEN PERFORM pg_notify('mlp_ws', NEW.workspace_id::text); END IF;
  RETURN NULL;
END $$;

CREATE OR REPLACE FUNCTION platform.notify_ws_outbox() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  PERFORM pg_notify('mlp_outbox', NEW.id::text);
  RETURN NULL;
END $$;

DROP TRIGGER IF EXISTS stream_notify ON ops.events;
CREATE TRIGGER stream_notify AFTER INSERT ON ops.events FOR EACH ROW EXECUTE FUNCTION platform.notify_ws();
DROP TRIGGER IF EXISTS stream_notify ON sched.jobs;
CREATE TRIGGER stream_notify AFTER INSERT OR UPDATE ON sched.jobs FOR EACH ROW EXECUTE FUNCTION platform.notify_ws();
DROP TRIGGER IF EXISTS stream_notify ON notify.notifications;
CREATE TRIGGER stream_notify AFTER INSERT OR UPDATE ON notify.notifications FOR EACH ROW EXECUTE FUNCTION platform.notify_ws();
DROP TRIGGER IF EXISTS stream_notify ON platform.outbox;
CREATE TRIGGER stream_notify AFTER INSERT ON platform.outbox FOR EACH ROW EXECUTE FUNCTION platform.notify_ws_outbox();
