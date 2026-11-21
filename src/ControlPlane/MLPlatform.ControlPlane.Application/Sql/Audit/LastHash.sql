-- mlp:query LastHash :scalar
SELECT hash FROM audit.events ORDER BY seq DESC LIMIT 1;
