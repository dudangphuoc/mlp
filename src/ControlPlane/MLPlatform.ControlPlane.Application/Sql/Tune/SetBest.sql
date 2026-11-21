-- mlp:query SetBest :exec
UPDATE tune.sweeps SET best_trial = @best_trial, best_value = @best_value WHERE id = @id;
