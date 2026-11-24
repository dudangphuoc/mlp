import mlplatform_proto as mlp
mlp.init()

import multiprocessing as mp
import os
from helper import scale


def work(step):
    return os.getpid(), scale(step)


if __name__ == "__main__":
    with mp.get_context("spawn").Pool(2) as pool:
        print(pool.map(work, range(2)), flush=True)
    print("done", flush=True)
