import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { Checklist, FileDrop, OptionCards } from './inputs'

function Recourse() {
  const [value, setValue] = useState<'A' | 'B'>('A')
  return (
    <OptionCards
      label="Regres"
      value={value}
      onChange={setValue}
      options={[
        { value: 'A', title: 'Bez regresa' },
        { value: 'B', title: 'Sa regresom' },
      ]}
    />
  )
}

describe('form controls', () => {
  it('option cards are a radio group the user can switch', async () => {
    render(<Recourse />)

    await userEvent.click(screen.getByText('Sa regresom'))

    expect(screen.getByRole('radio', { name: /Sa regresom/ })).toBeChecked()
    expect(screen.getByRole('radio', { name: /Bez regresa/ })).not.toBeChecked()
  })

  it('the drop zone uploads the picked file and shows its status', async () => {
    const upload = vi.fn(async () => ({}))
    render(<FileDrop label="Prateći dokument" onFile={upload} />)

    await userEvent.upload(
      screen.getByLabelText('Prateći dokument'),
      new File(['%PDF'], 'ugovor.pdf', { type: 'application/pdf' }),
    )

    expect(upload).toHaveBeenCalledOnce()
    expect(await screen.findByText('Priloženo')).toBeInTheDocument()
    expect(screen.getByText('ugovor.pdf')).toBeInTheDocument()
  })

  it('a failed upload is marked as rejected', async () => {
    render(<FileDrop label="Dokaz" onFile={() => Promise.reject(new Error('x'))} />)

    await userEvent.upload(screen.getByLabelText('Dokaz'), new File(['x'], 'a.zip'))

    expect(await screen.findByText('Odbijeno')).toBeInTheDocument()
  })

  it('the checklist counts the confirmed statements', () => {
    render(
      <Checklist
        title="Izjava"
        items={[
          { key: 'a', label: 'Prva' },
          { key: 'b', label: 'Druga' },
        ]}
        values={{ a: true }}
        onChange={() => {}}
      />,
    )

    expect(screen.getByText('1 od 2 potvrđeno')).toBeInTheDocument()
  })
})
