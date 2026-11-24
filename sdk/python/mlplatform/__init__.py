"""MLPlatform SDK for Python (standard library only).

    import mlplatform as mlp
    mlp.init()

    cfg = mlp.params(epochs=30, lr=1e-4)
    for step in range(cfg.epochs):
        mlp.log({"loss": 0.1}, step=step)
    mlp.save("outputs/")

`.env` next to the script (or above it): MLP_API_URL + MLP_API_KEY turn on tracking; without them the code runs untracked.
"""
from .errors import ConfigError, MlpError
from .inference import Client, MLPlatformError
from .tracking import Params, dataset, init, log, model, params, run, save

__version__ = "0.2.0"
__all__ = ["Client", "ConfigError", "MLPlatformError", "MlpError", "Params", "dataset", "init", "log", "model", "params", "run", "save"]
