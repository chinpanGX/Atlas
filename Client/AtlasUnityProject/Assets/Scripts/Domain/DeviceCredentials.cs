namespace Atlas.Domain
{
    public readonly struct DeviceCredentials
    {
        public readonly string DeviceId;
        public readonly string SecretKey;

        public DeviceCredentials(string deviceId, string secretKey)
        {
            DeviceId = deviceId;
            SecretKey = secretKey;
        }
    }
}
