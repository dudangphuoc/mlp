import mlplatform_proto as mlp
mlp.init()

import socket
import sys
from helper import scale

host = socket.gethostname()
total = 0
for step in range(3):
    total += scale(step)
    print(f"step {step} on {host} total={total}", flush=True)
print("done", flush=True)
sys.exit(3)
