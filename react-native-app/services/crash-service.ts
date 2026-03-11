import crashlytics from '@react-native-firebase/crashlytics';

class CrashService {
  private isEnabled = false;

  constructor() {
    try {
      crashlytics().setCrashlyticsCollectionEnabled(true);
      this.isEnabled = true;
    } catch (error) {
      this.isEnabled = false;
      console.warn('Crashlytics unavailable:', error);
    }
  }

  recordNonFatal(error: Error, context?: string): void {
    if (!this.isEnabled) return;

    if (context) {
      crashlytics().setAttribute('error_context', context);
    }

    crashlytics().recordError(error);
  }

  setMetadata(key: string, value: string): void {
    if (!this.isEnabled) return;
    crashlytics().setAttribute(key, value);
  }

  log(message: string): void {
    if (!this.isEnabled) return;
    crashlytics().log(message);
  }

  setUserId(userId: string): void {
    if (!this.isEnabled) return;
    crashlytics().setUserId(userId);
  }

  triggerNativeCrash(): void {
    crashlytics().crash();
  }
}

export const crashService = new CrashService();
