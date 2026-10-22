import { createContext, useContext } from 'react'

/** Runs an action; if the API answers step_up_required, asks for the authenticator code and runs it again. */
export type WithStepUp = <T>(action: () => Promise<T>) => Promise<T>

export const StepUpContext = createContext<WithStepUp>((action) => action())

export const useStepUp = () => useContext(StepUpContext)
