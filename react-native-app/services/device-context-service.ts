import AsyncStorage from '@react-native-async-storage/async-storage';
import DeviceInfo from 'react-native-device-info';
import {NativeModules, Platform} from 'react-native';

const USER_UUID_KEY = '@device_context_user_uuid';

interface DeviceContext {
  userUuid: string;
  countryIso: string;
  brand: string;
  deviceModel: string;
}

class DeviceContextService {
  private context: DeviceContext | null = null;

  async initialize(): Promise<DeviceContext> {
    if (this.context) return this.context;

    const [userUuid, countryIso, deviceModel] = await Promise.all([
      this.getOrCreateUserUuid(),
      this.resolveCountryIso(),
      DeviceInfo.getModel(),
    ]);

    this.context = {
      userUuid,
      countryIso,
      brand: 'Brand A',
      deviceModel,
    };

    return this.context;
  }

  getContext(): DeviceContext | null {
    return this.context;
  }

  private async getOrCreateUserUuid(): Promise<string> {
    try {
      const stored = await AsyncStorage.getItem(USER_UUID_KEY);
      if (stored) return stored;

      const generated = this.generateUuid();
      await AsyncStorage.setItem(USER_UUID_KEY, generated);
      return generated;
    } catch {
      return this.generateUuid();
    }
  }

  private resolveCountryIso(): string {
    try {
      if (Platform.OS === 'ios') {
        const locale =
          NativeModules.SettingsManager?.settings?.AppleLocale ||
          NativeModules.SettingsManager?.settings?.AppleLanguages?.[0] ||
          '';
        const regionMatch = locale.match(/[_-]([A-Z]{2})$/);
        if (regionMatch) return regionMatch[1];
      }

      const intlLocale = Intl.DateTimeFormat().resolvedOptions().locale || '';
      const parts = intlLocale.split('-');
      const lastPart = parts[parts.length - 1];
      if (lastPart && lastPart.length === 2) return lastPart.toUpperCase();

      return 'UNKNOWN';
    } catch {
      return 'UNKNOWN';
    }
  }

  private generateUuid(): string {
    const hex = '0123456789abcdef';
    let uuid = '';
    for (let position = 0; position < 36; position++) {
      if (position === 8 || position === 13 || position === 18 || position === 23) {
        uuid += '-';
      } else if (position === 14) {
        uuid += '4';
      } else if (position === 19) {
        uuid += hex[(Math.random() * 4) | 8];
      } else {
        uuid += hex[(Math.random() * 16) | 0];
      }
    }
    return uuid;
  }
}

export const deviceContextService = new DeviceContextService();
