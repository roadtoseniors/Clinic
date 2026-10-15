import { dotnet } from './_framework/dotnet.js';

try {
    const response = await fetch('./clinic.config.json', { cache: 'no-store' });
    if (!response.ok) throw new Error('Application configuration is unavailable.');
    const settings = await response.json();
    const runtime = await dotnet
        .withDiagnosticTracing(false)
        .withEnvironmentVariable('CLINIC_API_URL', settings.apiUrl)
        .withApplicationCulture('ru-RU')
        .create();
    const config = runtime.getConfig();
    await runtime.runMain(config.mainAssemblyName, []);
} catch (error) {
    console.error('Clinic startup failed:', error);
    document.getElementById('out').innerHTML = '<div class="avalonia-splash"><span class="clinic-mark">+</span><h1>Не удалось открыть приложение</h1><p>Проверьте подключение и попробуйте ещё раз.</p><button type="button" id="clinic-reload">Повторить</button></div>';
    document.getElementById('clinic-reload').addEventListener('click', () => location.reload());
}
