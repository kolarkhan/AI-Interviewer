package com.aiinterviewer.security;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;
import android.util.Log;

import java.nio.charset.StandardCharsets;
import java.security.KeyStore;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/**
 * Minimal secure key/value store for the Unity app.
 * - AES-256-GCM key generated inside the Android Keystore (non-exportable, API 23+).
 * - Ciphertext (IV + data) stored in a private SharedPreferences file.
 * Called from C# via AndroidJavaClass (see SecureStorage.cs).
 */
public final class SecureStore {
    private static final String TAG = "AIISecureStore";
    private static final String KEYSTORE = "AndroidKeyStore";
    private static final String KEY_ALIAS = "ai_interviewer_secure_store_v1";
    private static final String PREFS = "ai_interviewer_secure_store";
    private static final int IV_LENGTH = 12;
    private static final int TAG_BITS = 128;

    private SecureStore() {}

    private static SecretKey getOrCreateKey() throws Exception {
        KeyStore ks = KeyStore.getInstance(KEYSTORE);
        ks.load(null);
        if (ks.containsAlias(KEY_ALIAS)) {
            return ((KeyStore.SecretKeyEntry) ks.getEntry(KEY_ALIAS, null)).getSecretKey();
        }
        KeyGenerator gen = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, KEYSTORE);
        gen.init(new KeyGenParameterSpec.Builder(KEY_ALIAS,
                KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build());
        return gen.generateKey();
    }

    private static SharedPreferences prefs(Context ctx) {
        return ctx.getApplicationContext().getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    public static boolean put(Context ctx, String key, String value) {
        try {
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.ENCRYPT_MODE, getOrCreateKey());
            byte[] iv = cipher.getIV();
            byte[] enc = cipher.doFinal(value.getBytes(StandardCharsets.UTF_8));
            byte[] out = new byte[iv.length + enc.length];
            System.arraycopy(iv, 0, out, 0, iv.length);
            System.arraycopy(enc, 0, out, iv.length, enc.length);
            return prefs(ctx).edit().putString(key, Base64.encodeToString(out, Base64.NO_WRAP)).commit();
        } catch (Exception e) {
            Log.e(TAG, "put failed: " + e.getClass().getSimpleName());
            return false;
        }
    }

    public static String get(Context ctx, String key) {
        String stored = prefs(ctx).getString(key, null);
        if (stored == null) return null;
        try {
            byte[] all = Base64.decode(stored, Base64.NO_WRAP);
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE, getOrCreateKey(), new GCMParameterSpec(TAG_BITS, all, 0, IV_LENGTH));
            byte[] plain = cipher.doFinal(all, IV_LENGTH, all.length - IV_LENGTH);
            return new String(plain, StandardCharsets.UTF_8);
        } catch (Exception e) {
            // Key invalidated (e.g. device restore) or data corrupted: treat as signed out.
            Log.w(TAG, "get failed, clearing entry: " + e.getClass().getSimpleName());
            prefs(ctx).edit().remove(key).commit();
            return null;
        }
    }

    public static boolean remove(Context ctx, String key) {
        return prefs(ctx).edit().remove(key).commit();
    }
}
