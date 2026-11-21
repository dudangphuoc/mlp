-- Machine VPN status (mlp-net overlay): the control plane checks the tunnel to the hub after bringing up the spoke.
ALTER TABLE fleet.hosts ADD COLUMN IF NOT EXISTS vpn_status text NOT NULL DEFAULT 'pending';   -- pending | connected | failed
ALTER TABLE fleet.hosts ADD COLUMN IF NOT EXISTS vpn_message text;
ALTER TABLE fleet.hosts ADD COLUMN IF NOT EXISTS vpn_checked_at timestamptz;
