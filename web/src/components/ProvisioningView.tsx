import { QRCodeSVG } from 'qrcode.react'
import type { Provisioning } from '../api/auth'

/** The TOTP provisioning QR code and the key for manual entry. */
export function ProvisioningView({ provisioning }: { provisioning: Provisioning }) {
  return (
    <div className="space-y-2">
      <div className="flex justify-center">
        <QRCodeSVG
          value={provisioning.otpauthUri}
          size={180}
          title="QR kod za aplikaciju za autentifikaciju"
        />
      </div>
      <p className="text-center text-xs text-slate-600">
        Ključ za ručni unos: <code className="break-all">{provisioning.sharedKey}</code>
      </p>
    </div>
  )
}
